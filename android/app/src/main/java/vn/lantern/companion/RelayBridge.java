package vn.lantern.companion;

import org.json.*;
import android.util.Base64;
import javax.net.ssl.HttpsURLConnection;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;

/** Only the loopback socket is exposed. It carries opaque TLS bytes, never decrypted chat. */
final class RelayBridge implements AutoCloseable {
    private final JSONObject config;
    private final String session=UUID.randomUUID().toString().replace("-","");
    private final BlockingQueue<byte[]> outgoing=new ArrayBlockingQueue<>(32);
    private final ServerSocket listener;
    private volatile Socket peer; private volatile boolean closed;
    private volatile long received;
    RelayBridge(JSONObject config) throws Exception {
        this.config=config; call("open",new JSONObject().put("session",session));
        listener=new ServerSocket(0,1,InetAddress.getByName("127.0.0.1")); listener.setSoTimeout(15000);
        Thread thread=new Thread(this::run,"lantern-relay"); thread.setDaemon(true); thread.start();
    }
    int port(){return listener.getLocalPort();}
    private JSONObject call(String action,JSONObject body) throws Exception {
        URL url=new URL(config.getString("url")+"v1/routes/"+config.getString("id")+"/"+action);
        HttpsURLConnection http=(HttpsURLConnection)url.openConnection();
        try {
            http.setInstanceFollowRedirects(false); http.setConnectTimeout(15000); http.setReadTimeout(20000); http.setRequestMethod("POST"); http.setDoOutput(true);
            http.setRequestProperty("Authorization","Bearer "+config.getString("key"));
            if(!config.optString("token").isEmpty())http.setRequestProperty("OAI-Sites-Authorization","Bearer "+config.getString("token"));
            http.setRequestProperty("Content-Type","application/json");
            byte[] bytes=body.toString().getBytes(StandardCharsets.UTF_8); http.setFixedLengthStreamingMode(bytes.length);
            try(OutputStream out=http.getOutputStream()){out.write(bytes);}
            int status=http.getResponseCode(); if(status!=200)throw new RelayFailure(status);
            try(InputStream in=http.getInputStream()){ByteArrayOutputStream out=new ByteArrayOutputStream(); byte[] buffer=new byte[8192]; int n;while((n=in.read(buffer))>=0){out.write(buffer,0,n);if(out.size()>275000)throw new IOException("Relay response too large");} return new JSONObject(out.toString("UTF-8"));}
        } finally {http.disconnect();}
    }
    private static final class RelayFailure extends IOException {final int status;RelayFailure(int status){this.status=status;}}
    private void run(){
        Thread reader=null,sender=null;
        try {
            peer=listener.accept(); listener.close(); peer.setTcpNoDelay(true);
            reader=new Thread(()->{try{InputStream in=peer.getInputStream();byte[] buffer=new byte[32768];int n;while(!closed&&(n=in.read(buffer))>0)outgoing.put(Arrays.copyOf(buffer,n));}catch(Exception ignored){}finally{close();}},"lantern-relay-write");reader.setDaemon(true);reader.start();
            sender=new Thread(this::sendLoop,"lantern-relay-push");sender.setDaemon(true);sender.start();
            OutputStream out=peer.getOutputStream(); long lastSuccess=System.nanoTime();
            while(!closed){
                JSONObject body=new JSONObject().put("session",session).put("after",received).put("batch",4);
                JSONObject response;
                try{response=call("exchange",body);lastSuccess=System.nanoTime();}
                catch(Exception error){if(error instanceof RelayFailure&&((RelayFailure)error).status!=429)throw error;if(System.nanoTime()-lastSuccess>45_000_000_000L)throw error;Thread.sleep(700);continue;}
                JSONArray packets=response.getJSONArray("packets");
                for(int i=0;i<packets.length();i++){JSONObject packet=packets.getJSONObject(i);long next=packet.getLong("seq");if(next!=received+1)throw new IOException("Relay sequence gap");byte[] data=Base64.decode(packet.getString("data"),Base64.NO_WRAP);if(data.length>49152)throw new IOException();out.write(data);out.flush();received=next;}
                if(packets.length()==0)Thread.sleep(50);
            }
        }catch(Exception ignored){}finally{close();if(reader!=null)reader.interrupt();if(sender!=null)sender.interrupt();}
    }
    private void sendLoop(){try{long seq=0;while(!closed){byte[] first=outgoing.take();ByteArrayOutputStream bytes=new ByteArrayOutputStream();bytes.write(first);while(outgoing.peek()!=null&&bytes.size()+outgoing.peek().length<=49152)bytes.write(outgoing.take());JSONObject body=new JSONObject().put("session",session).put("seq",++seq).put("pushOnly",true).put("data",Base64.encodeToString(bytes.toByteArray(),Base64.NO_WRAP));long start=System.nanoTime();while(!closed)try{body.put("after",received);call("exchange",body);break;}catch(Exception e){if(e instanceof RelayFailure&&((RelayFailure)e).status!=429||System.nanoTime()-start>45_000_000_000L)throw e;Thread.sleep(150);}}}catch(Exception ignored){}finally{close();}}
    public void close(){closed=true;try{listener.close();}catch(Exception ignored){}try{if(peer!=null)peer.close();}catch(Exception ignored){}}
}
