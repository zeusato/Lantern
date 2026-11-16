package vn.lantern.companion;

import android.app.*;
import android.content.*;
import android.os.*;
import android.net.Uri;
import android.util.Base64;
import org.json.*;
import java.io.*;
import java.security.MessageDigest;
import java.util.*;
import java.util.concurrent.*;
import java.util.function.Consumer;

public final class ConnectionService extends Service {
    static volatile ConnectionService current;
    static volatile Runnable changed;
    static volatile boolean visible;
    static volatile String selected="",status="Chưa kết nối",account="";
    static volatile JSONArray conversations=new JSONArray(),messages=new JSONArray();
    static final ConcurrentHashMap<String,File> images=new ConcurrentHashMap<>();
    private final BlockingQueue<Work> work=new ArrayBlockingQueue<>(24);
    private volatile boolean stopping; private volatile PcConnection pc; private Thread worker;
    private final Handler ui=new Handler(Looper.getMainLooper());
    interface Job { void run(PcConnection connection) throws Exception; }
    private record Work(Job job,Consumer<String> done){}
    static JSONObject packet(String type,String conversation) throws JSONException {return new JSONObject().put("type",type).put("conversation",conversation);}
    private void notifyUi(){ui.post(()->{if(changed!=null)changed.run();});}
    public IBinder onBind(Intent intent){return null;}
    public void onCreate(){super.onCreate();current=this;clearMedia();}
    public int onStartCommand(Intent intent,int flags,int id){
        NotificationManager manager=getSystemService(NotificationManager.class);manager.createNotificationChannel(new NotificationChannel("connection","Kết nối PC",NotificationManager.IMPORTANCE_LOW));
        PendingIntent open=PendingIntent.getActivity(this,0,new Intent(this,MainActivity.class),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
        Intent stop=new Intent(this,ConnectionService.class).setAction("stop");PendingIntent stopAction=PendingIntent.getService(this,1,stop,PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
        startForeground(1,new Notification.Builder(this,"connection").setSmallIcon(R.drawable.ic_lantern).setContentTitle("Lantern · Kết nối điện thoại").setContentText("PC cần mở Lantern để gửi và nhận tin").setContentIntent(open).setOngoing(true).addAction(new Notification.Action.Builder(null,"Ngắt kết nối",stopAction).build()).build());
        if(intent!=null&&"stop".equals(intent.getAction())){stopSelf();return START_NOT_STICKY;}
        if(worker==null){worker=new Thread(this::loop,"lantern-companion");worker.start();}return START_NOT_STICKY;
    }
    private void loop(){
        while(!stopping){
            try {
                JSONObject config=DeviceKeys.load(this);if(config==null)break;
                status="Đang nối với PC…";notifyUi(); pc=new PcConnection(config);
                config.put("nonce","");DeviceKeys.save(this,config);account=config.optString("name","Lantern");status="Đã kết nối · "+account;notifyUi();
                while(!stopping){
                    Work task=work.poll(200,TimeUnit.MILLISECONDS);
                    if(task!=null){try{task.job.run(pc);ui.post(()->task.done.accept(null));}catch(Exception e){ui.post(()->task.done.accept(e instanceof RemoteError?e.getMessage():"Kết nối bị ngắt. Kiểm tra lịch sử trước khi gửi lại."));if(!(e instanceof RemoteError))throw e;}}
                    JSONObject list=checked(pc.request(packet("list","")));conversations=list.optJSONArray("conversations");if(conversations==null)conversations=new JSONArray();
                    String conversation=selected;boolean locked=true;
                    for(int i=0;i<conversations.length();i++){JSONObject c=conversations.getJSONObject(i);if(c.getString("id").equals(conversation))locked=c.optBoolean("locked");}
                    if(visible&&!conversation.isEmpty()&&!locked){
                        JSONObject history=checked(pc.request(packet("history",conversation)));if(selected.equals(conversation)){messages=history.getJSONArray("messages");notifyUi();autoImage(conversation);
                            JSONArray read=new JSONArray();for(int i=0;i<messages.length();i++){JSONObject m=messages.getJSONObject(i);if(!"image".equals(m.optString("kind"))||images.containsKey(m.getString("id")))read.put(m.getString("id"));}
                            if(visible&&selected.equals(conversation))checked(pc.request(packet("read",conversation).put("visibleIds",read)));
                        }
                    }else {messages=new JSONArray();clearMedia();}
                    notifyUi();Thread.sleep(1200);
                }
            }catch(Exception error){status="PC chưa sẵn sàng · Đang thử kết nối lại";messages=new JSONArray();conversations=new JSONArray();clearMedia();notifyUi();}
            finally{PcConnection connection=pc;pc=null;if(connection!=null)connection.close();Work task;while((task=work.poll())!=null){Work dropped=task;ui.post(()->dropped.done.accept("Mất kết nối. Thao tác chưa được thực hiện."));}}
            if(!stopping)try{Thread.sleep(4000);}catch(InterruptedException ignored){}
        }
        stopSelf();
    }
    private static final class RemoteError extends Exception{RemoteError(String message){super(message);}}
    private static JSONObject checked(JSONObject response)throws Exception{if("error".equals(response.optString("type")))throw new RemoteError(response.optString("text","PC từ chối thao tác"));return response;}
    void submit(JSONObject request,Consumer<String> done){enqueue(connection->checked(connection.request(request)),done);}
    private void enqueue(Job task,Consumer<String> done){if(pc==null||!work.offer(new Work(task,done)))done.accept("Chưa kết nối với PC. Hãy đợi rồi thử lại.");}
    void download(JSONObject message,Consumer<File> done,Consumer<String> error){String conversation=selected;enqueue(connection->{File file=downloadNow(connection,conversation,message);ui.post(()->done.accept(file));},e->{if(e!=null)error.accept(e);});}
    private File downloadNow(PcConnection connection,String conversation,JSONObject message)throws Exception{
        long size=message.getLong("fileSize");if(size>100L*1024*1024)throw new RemoteError("Bản mobile hỗ trợ tệp tối đa 100 MB.");
        String id=message.getString("id");if(!id.matches("[A-Fa-f0-9]{32,64}"))throw new IOException();
        File dir=new File(getCacheDir(),"attachments");dir.mkdirs();String name=message.optString("fileName","tep").replaceAll("[^\\p{L}\\p{N}._-]","_");if(name.length()>100)name=name.substring(name.length()-100);
        File file=new File(dir,id+"_"+name),part=new File(dir,id+".part");MessageDigest hash=MessageDigest.getInstance("SHA-256");
        try(FileOutputStream out=new FileOutputStream(part)){
            long offset=0;while(offset<size){JSONObject request=packet("download",conversation).put("messageId",id).put("offset",offset);JSONObject response=checked(connection.request(request));byte[] data=Base64.decode(response.getString("data"),Base64.NO_WRAP);if(data.length==0||data.length>32768||offset+data.length>size)throw new IOException("Tệp bị thiếu dữ liệu");out.write(data);hash.update(data);offset+=data.length;}
        }catch(Exception e){part.delete();throw e;}
        if(!DeviceKeys.hex(hash.digest()).equalsIgnoreCase(message.getString("hash"))){part.delete();throw new IOException("Checksum không khớp");}
        if(!part.renameTo(file)){part.delete();throw new IOException("Không lưu được tệp");}return file;
    }
    private void autoImage(String conversation)throws Exception{
        for(int i=messages.length()-1;i>=0;i--){JSONObject message=messages.getJSONObject(i);String id=message.getString("id");
            if("image".equals(message.optString("kind"))&&message.optLong("fileSize")<=10*1024*1024&&!images.containsKey(id)&&("received".equals(message.optString("status"))||message.optBoolean("outgoing"))){
                try{File file=downloadNow(pc,conversation,message);if(selected.equals(conversation)&&visible){images.put(id,file);notifyUi();}else file.delete();}catch(RemoteError ignored){}break;
            }
        }
        Set<String> active=new HashSet<>();for(int i=0;i<messages.length();i++)active.add(messages.getJSONObject(i).getString("id"));
        for(String key:images.keySet())if(!active.contains(key)){File file=images.remove(key);if(file!=null)file.delete();}
    }
    void upload(Uri uri,String name,Consumer<String> done){String conversation=selected;enqueue(connection->{
        File temp=File.createTempFile("upload-",".tmp",getCacheDir());try{
            MessageDigest digest=MessageDigest.getInstance("SHA-256");long size=0;
            try(InputStream in=getContentResolver().openInputStream(uri);FileOutputStream out=new FileOutputStream(temp)){if(in==null)throw new IOException();byte[] bytes=new byte[32768];int n;while((n=in.read(bytes))>=0){size+=n;if(size>100L*1024*1024)throw new RemoteError("Tệp tối đa 100 MB.");out.write(bytes,0,n);digest.update(bytes,0,n);}}
            checked(connection.request(packet("upload-start",conversation).put("fileName",name).put("size",size).put("hash",DeviceKeys.hex(digest.digest()))));
            try(FileInputStream in=new FileInputStream(temp)){byte[] bytes=new byte[32768];int n;long offset=0;while((n=in.read(bytes))>=0){checked(connection.request(packet("upload-chunk",conversation).put("offset",offset).put("data",Base64.encodeToString(bytes,0,n,Base64.NO_WRAP))));offset+=n;}}
            checked(connection.request(packet("upload-finish",conversation).put("fileName",name)));
        }finally{temp.delete();}
    },done);}
    static void clearMedia(){for(File file:images.values())file.delete();images.clear();}
    public void onDestroy(){stopping=true;PcConnection connection=pc;if(connection!=null)connection.close();if(worker!=null)worker.interrupt();current=null;status="Đã ngắt kết nối";messages=new JSONArray();conversations=new JSONArray();clearMedia();notifyUi();super.onDestroy();}
}
