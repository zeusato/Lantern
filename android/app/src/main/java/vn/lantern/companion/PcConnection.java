package vn.lantern.companion;

import org.json.*;
import javax.net.ssl.SSLSocket;
import java.net.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.UUID;
import java.util.concurrent.*;

final class PcConnection implements AutoCloseable {
    private SSLSocket socket; private RelayBridge relay; private DataInputStream input; private DataOutputStream output;
    boolean fastSync,pushSync;
    volatile java.util.function.Consumer<JSONObject> onPush=ignored->{};
    private final ConcurrentHashMap<String,CompletableFuture<JSONObject>> pending=new ConcurrentHashMap<>();
    private volatile boolean closed;
    private final Object writeGate=new Object();
    static JSONObject validateQr(String text) throws Exception {
        if(text.length()>12000)throw new IOException("Mã QR không hợp lệ");JSONObject config=new JSONObject(text);
        if(config.getInt("v")!=1||!config.getString("pc").matches("[A-F0-9]{64}")||!config.getString("nonce").matches("[A-Za-z0-9_-]{43}")||config.getLong("expires")<System.currentTimeMillis()/1000)throw new IOException("QR không hợp lệ hoặc đã hết hạn");
        if(config.getInt("port")<1||config.getInt("port")>65535||config.getJSONArray("addresses").length()>16)throw new IOException("Địa chỉ PC không hợp lệ");
        JSONObject r=config.optJSONObject("relay");if(r!=null){URI uri=new URI(r.getString("url"));if(!"https".equals(uri.getScheme())||uri.getHost()==null||uri.getUserInfo()!=null||uri.getQuery()!=null||uri.getFragment()!=null||!"/".equals(uri.getPath())||!r.getString("id").matches("[a-f0-9]{32}")||!r.getString("key").matches("[A-Za-z0-9_-]{43}")||r.getString("token").length()>6000)throw new IOException("Relay không hợp lệ");}
        return config;
    }
    private static boolean lan(String address){
        if(!address.matches("[0-9.]{7,15}"))return false;try{byte[] ip=InetAddress.getByName(address).getAddress();int a=ip[0]&255,b=ip[1]&255;return ip.length==4&&(a==10||a==192&&b==168||a==172&&b>=16&&b<=31||a==169&&b==254);}catch(Exception e){return false;}
    }
    PcConnection(JSONObject config) throws Exception {this(config,ignored->{});}
    PcConnection(JSONObject config,java.util.function.Consumer<String> progress) throws Exception {
        Exception last=null;JSONArray addresses=config.getJSONArray("addresses");
        progress.accept("Đang tìm PC trong Wi-Fi/LAN…");
        for(int i=0;i<addresses.length();i++){
            String address=addresses.getString(i);if(!lan(address))continue;
            try {connect(address,config.getInt("port"),config.getString("pc"),2500);last=null;break;}
            catch(Exception e){last=e;closeSocket();}
        }
        if(socket==null){JSONObject r=config.optJSONObject("relay");if(r==null)throw new IOException("Không tới được PC trong LAN",last);progress.accept("Đang kết nối PC qua Internet…");relay=new RelayBridge(r);try{connect("127.0.0.1",relay.port(),config.getString("pc"),10000);}catch(Exception e){close();throw e;}}
        input=new DataInputStream(socket.getInputStream());output=new DataOutputStream(new BufferedOutputStream(socket.getOutputStream(),65536));
        try {
            progress.accept(config.optString("nonce").isEmpty()?"Đang xác nhận thiết bị với PC…":"Đang chờ xác nhận ghép đôi trên PC…");
            JSONObject hello=new JSONObject().put("type","hello").put("nonce",config.optString("nonce")).put("name",android.os.Build.MANUFACTURER+" "+android.os.Build.MODEL).put("capabilities",new JSONArray().put("push-v1"));
            JSONObject welcome=request(hello);if(!"welcome".equals(welcome.optString("type")))throw new IOException("PC chưa chấp nhận ghép đôi");
            JSONArray capabilities=welcome.optJSONArray("capabilities");if(capabilities!=null)for(int i=0;i<capabilities.length();i++){if("sync-v1".equals(capabilities.optString(i)))fastSync=true;if("push-v1".equals(capabilities.optString(i)))pushSync=true;}
            socket.setSoTimeout(30000);
            if(pushSync){Thread reader=new Thread(this::readLoop,"lantern-pc-events");reader.setDaemon(true);reader.start();}
        }catch(Exception e){close();throw e;}
    }
    private void connect(String address,int port,String pin,int timeout) throws Exception {
        Socket raw=new Socket();try{raw.setTcpNoDelay(true);raw.connect(new InetSocketAddress(address,port),timeout);raw.setSoTimeout(timeout==2500?8000:45000);socket=(SSLSocket)DeviceKeys.tls(pin).createSocket(raw,address,port,true);socket.setSoTimeout(timeout==2500?8000:45000);socket.startHandshake();socket.setSoTimeout(120000);}catch(Exception e){raw.close();throw e;}
    }
    JSONObject request(JSONObject request) throws Exception {
        if(!request.has("id"))request.put("id",UUID.randomUUID().toString().replace("-",""));byte[] bytes=request.toString().getBytes(StandardCharsets.UTF_8);
        if(bytes.length>2*1024*1024)throw new IOException("Nội dung quá lớn");
        if(!pushSync)synchronized(writeGate){output.writeInt(bytes.length);output.write(bytes);output.flush();return read();}
        String id=request.getString("id");CompletableFuture<JSONObject> reply=new CompletableFuture<>();pending.put(id,reply);
        try{ synchronized(writeGate){if(closed)throw new EOFException();output.writeInt(bytes.length);output.write(bytes);output.flush();}return reply.get(30,TimeUnit.SECONDS);}
        catch(ExecutionException e){if(e.getCause() instanceof Exception cause)throw cause;throw new IOException(e);}
        finally{pending.remove(id);}
    }
    private JSONObject read()throws Exception{int length=input.readInt();if(length<2||length>2*1024*1024)throw new IOException("Gói PC không hợp lệ");byte[] response=new byte[length];input.readFully(response);return new JSONObject(new String(response,StandardCharsets.UTF_8));}
    private void readLoop(){try{while(!closed){JSONObject response=read();String id=response.optString("id");if(id.isEmpty())onPush.accept(response);else{CompletableFuture<JSONObject> reply=pending.get(id);if(reply!=null)reply.complete(response);}}}catch(Exception error){for(CompletableFuture<JSONObject> reply:pending.values())reply.completeExceptionally(error);}finally{close();}}
    private void closeSocket(){try{if(socket!=null)socket.close();}catch(Exception ignored){}socket=null;}
    String transport(){return relay==null?"Wi-Fi/LAN":"Internet";}
    public void close(){closed=true;closeSocket();if(relay!=null)relay.close();for(CompletableFuture<JSONObject> reply:pending.values())reply.completeExceptionally(new EOFException());}
}
