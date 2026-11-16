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
            http.setRequestProperty("Authorization","Bearer "+config.getString("key")); http.setRequestProperty("OAI-Sites-Authorization","Bearer "+config.getString("token")); http.setRequestProperty("Content-Type","application/json");
            byte[] bytes=body.toString().getBytes(StandardCharsets.UTF_8); http.setFixedLengthStreamingMode(bytes.length);
            try(OutputStream out=http.getOutputStream()){out.write(bytes);}
            int status=http.getResponseCode(); if(status!=200)throw new RelayFailure(status);
            try(InputStream in=http.getInputStream()){ByteArrayOutputStream out=new ByteArrayOutputStream(); byte[] buffer=new byte[8192]; int n;while((n=in.read(buffer))>=0){out.write(buffer,0,n);if(out.size()>90000)throw new IOException("Relay response too large");} return new JSONObject(out.toString("UTF-8"));}
        } finally {http.disconnect();}
    }
    private static final class RelayFailure extends IOException {final int status;RelayFailure(int status){this.status=status;}}
    private void run(){
        Thread reader=null;
        try {
            peer=listener.accept(); listener.close(); peer.setTcpNoDelay(true);
            reader=new Thread(()->{try{InputStream in=peer.getInputStream();byte[] buffer=new byte[32768];int n;while(!closed&&(n=in.read(buffer))>0)outgoing.put(Arrays.copyOf(buffer,n));}catch(Exception ignored){}finally{close();}},"lantern-relay-write");reader.setDaemon(true);reader.start();
            OutputStream out=peer.getOutputStream(); long after=0,seq=0,lastSuccess=System.nanoTime();byte[] pending=null;
            while(!closed){
                if(pending==null){pending=outgoing.poll();if(pending!=null)seq++;}
                JSONObject body=new JSONObject().put("session",session).put("after",after);
                if(pending!=null)body.put("seq",seq).put("data",Base64.encodeToString(pending,Base64.NO_WRAP));
                JSONObject response;
                try{response=call("exchange",body);lastSuccess=System.nanoTime();}
                catch(Exception error){if(error instanceof RelayFailure&&((RelayFailure)error).status!=429)throw error;if(System.nanoTime()-lastSuccess>45_000_000_000L)throw error;Thread.sleep(700);continue;}
                pending=null;JSONArray packets=response.getJSONArray("packets");
                if(packets.length()>0){JSONObject packet=packets.getJSONObject(0);long next=packet.getLong("seq");if(next!=after+1)throw new IOException("Relay sequence gap");byte[] data=Base64.decode(packet.getString("data"),Base64.NO_WRAP);if(data.length>49152)throw new IOException();out.write(data);out.flush();after=next;}
                else if(outgoing.isEmpty())Thread.sleep(350);
            }
        }catch(Exception ignored){}finally{close();if(reader!=null)reader.interrupt();}
    }
    public void close(){closed=true;try{listener.close();}catch(Exception ignored){}try{if(peer!=null)peer.close();}catch(Exception ignored){}}
}
