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
    static volatile String problem="";
    static volatile boolean connected;
    static volatile JSONArray conversations=new JSONArray(),messages=new JSONArray();
    record ReadState(String conversation,Set<String> ids){}
    static volatile ReadState displayed=new ReadState("",Collections.emptySet());
    static final ConcurrentHashMap<String,File> images=new ConcurrentHashMap<>();
    private final BlockingQueue<Work> work=new ArrayBlockingQueue<>(24);
    private volatile boolean stopping; private volatile PcConnection pc; private Thread worker;
    private HistoryCache cache;
    private final ConcurrentHashMap<String,JSONArray> snapshots=new ConcurrentHashMap<>();
    private final Map<String,String> historyHashes=new HashMap<>();
    private final Set<String> failedImages=new HashSet<>();
    private String savedContacts="";
    private final Handler ui=new Handler(Looper.getMainLooper());
    interface Job { void run(PcConnection connection) throws Exception; }
    private record Work(Job job,Consumer<String> done,boolean transfer){}
    static JSONObject packet(String type,String conversation) throws JSONException {return new JSONObject().put("type",type).put("conversation",conversation);}
    private void notifyUi(){ui.post(()->{if(changed!=null)changed.run();});}
    public IBinder onBind(Intent intent){return null;}
    public void onCreate(){super.onCreate();current=this;clearMedia();try{JSONObject config=DeviceKeys.load(this);if(config!=null){cache=new HistoryCache(this,config.getString("pc"));conversations=cache.conversations();restoreSelected();}}catch(Exception ignored){}}
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
                status="Đang nối với PC…";notifyUi(); pc=new PcConnection(config,value->{if(!stopping){status=value;notifyUi();}});if(stopping){pc.close();break;}
                config.put("nonce","");DeviceKeys.save(this,config);account=config.optString("name","Lantern");connected=true;problem="";status="Đã kết nối qua "+pc.transport()+" · "+account;notifyUi();
                long wait=0;
                while(!stopping){
                    Work task=work.poll(wait,TimeUnit.MILLISECONDS);if(task!=null)perform(task,pc);
                    JSONObject list=checked(pc.request(packet("list","")));conversations=list.optJSONArray("conversations");if(conversations==null)conversations=new JSONArray();
                    if(cache!=null&&!conversations.toString().equals(savedContacts))try{cache.saveConversations(conversations);savedContacts=conversations.toString();}catch(Exception ignored){}
                    String conversation=selected;boolean locked=true;
                    for(int i=0;i<conversations.length();i++){JSONObject c=conversations.getJSONObject(i);if(c.getString("id").equals(conversation))locked=c.optBoolean("locked");}
                    if(visible&&!conversation.isEmpty()&&!locked){
                        JSONObject history=checked(pc.request(packet("history",conversation).put("hash",historyHashes.getOrDefault(conversation,""))));if(selected.equals(conversation)){
                            if(!"unchanged".equals(history.optString("type"))){messages=history.getJSONArray("messages");snapshots.put(conversation,messages);historyHashes.put(conversation,history.optString("hash"));if(cache!=null)try{cache.saveMessages(conversation,messages);}catch(Exception ignored){}}else messages=snapshots.getOrDefault(conversation,new JSONArray());
                            notifyUi();autoImage(conversation);
                            ReadState shown=displayed;JSONArray batch=messages,read=new JSONArray();for(int i=0;i<batch.length();i++){JSONObject m=batch.getJSONObject(i);if(shown.conversation().equals(conversation)&&shown.ids().contains(m.getString("id"))&&(!"image".equals(m.optString("kind"))||images.containsKey(m.getString("id"))))read.put(m.getString("id"));}
                            if(read.length()>0&&visible&&selected.equals(conversation))checked(pc.request(packet("read",conversation).put("visibleIds",read)));
                        }
                    }else {messages=new JSONArray();clearMedia();}
                    notifyUi();wait=1200;
                }
            }catch(Exception error){if(stopping)break;connected=false;problem=connectionError(error);status="Đang kết nối lại · "+problem;historyHashes.clear();snapshots.clear();conversations=cache==null?new JSONArray():cache.conversations();clearMedia();restoreSelected();notifyUi();}
            finally{if(current==this)connected=false;PcConnection connection=pc;pc=null;if(connection!=null)connection.close();Work task;while((task=work.poll())!=null){Work dropped=task;ui.post(()->dropped.done.accept("Mất kết nối. Thao tác này chưa được gửi tới PC."));}}
            if(!stopping)try{Thread.sleep(4000);}catch(InterruptedException ignored){}
        }
        if(!stopping)stopSelf();
    }
    private static final class RemoteError extends Exception{RemoteError(String message){super(message);}}
    private static String connectionError(Exception error){for(Throwable e=error;e!=null;e=e.getCause()){if(e instanceof java.net.SocketTimeoutException)return "PC phản hồi quá chậm";if(e instanceof java.net.UnknownHostException)return "Không tìm thấy địa chỉ relay";if(e instanceof javax.net.ssl.SSLHandshakeException)return "Không xác thực được kết nối với PC";if(e instanceof java.net.ConnectException)return "Chưa tới được cổng kết nối PC";if(e instanceof java.io.EOFException)return "PC đã đóng kết nối";}return error instanceof RemoteError?error.getMessage():"Kết nối bị gián đoạn";}
    private void perform(Work task,PcConnection connection)throws Exception{try{task.job.run(connection);ui.post(()->task.done.accept(null));}catch(Exception e){ui.post(()->task.done.accept(e instanceof RemoteError?e.getMessage():"Chưa nhận được xác nhận từ PC. Thử lại sẽ dùng cùng mã gửi để tránh trùng."));if(!(e instanceof RemoteError))throw e;}}
    private static JSONObject checked(JSONObject response)throws Exception{if("error".equals(response.optString("type")))throw new RemoteError(response.optString("text","PC từ chối thao tác"));return response;}
    void submit(JSONObject request,Consumer<String> done){try{boolean send="send".equals(request.optString("type"));if(send&&!request.has("id"))request.put("id",cache==null?UUID.randomUUID().toString().replace("-",""):cache.sendId(request.getString("conversation"),request.getString("text"),request.optString("replyTo")));enqueue(connection->{checked(connection.request(request));if(send&&cache!=null)cache.confirmSend(request.getString("conversation"),request.getString("id"));},done);}catch(Exception e){done.accept("Không lưu được mã gửi an toàn. Hãy thử lại.");}}
    private void enqueue(Job task,Consumer<String> done){enqueue(task,done,false);}
    private void enqueue(Job task,Consumer<String> done,boolean transfer){if(!connected||pc==null)done.accept("Đang nối lại PC. Nội dung Đại ca nhập vẫn được giữ.");else if(!work.offer(new Work(task,done,transfer)))done.accept("Đang xử lý các thao tác trước. Đại ca đợi một chút.");}
    private void interactiveCommand(PcConnection connection)throws Exception{for(Work command:work)if(!command.transfer&&work.remove(command)){perform(command,connection);break;}}
    void select(String conversation){selected=conversation;clearMedia();restoreSelected();restoreCachedImages(conversation,messages);notifyUi();}
    private void restoreCachedImages(String conversation,JSONArray batch){if(cache==null||selectedLocked())return;Thread loader=new Thread(()->{for(int i=0;i<batch.length()&&!stopping&&conversation.equals(selected)&&!selectedLocked();i++){JSONObject message=batch.optJSONObject(i);if(message==null||!"image".equals(message.optString("kind")))continue;File file=new File(getCacheDir(),"preview-"+message.optString("id"));try{if(cache.restoreImage(message,file)){if(conversation.equals(selected)&&!selectedLocked()){images.put(message.getString("id"),file);notifyUi();}else file.delete();}}catch(Exception ignored){}}},"lantern-cache");loader.setDaemon(true);loader.start();}
    private boolean selectedLocked(){for(int i=0;i<conversations.length();i++){JSONObject c=conversations.optJSONObject(i);if(c!=null&&selected.equals(c.optString("id")))return c.optBoolean("locked",true);}return true;}
    private void restoreSelected(){messages=selectedLocked()?new JSONArray():snapshots.getOrDefault(selected,cache==null?new JSONArray():cache.messages(selected));}
    void download(JSONObject message,Consumer<File> done,Consumer<String> error){String conversation=selected;enqueue(connection->{File file=downloadNow(connection,conversation,message);ui.post(()->done.accept(file));},e->{if(e!=null)error.accept(e);},true);}
    private File downloadNow(PcConnection connection,String conversation,JSONObject message)throws Exception{return downloadNow(connection,conversation,message,false);}
    private File downloadNow(PcConnection connection,String conversation,JSONObject message,boolean background)throws Exception{
        long size=message.getLong("fileSize");if(size>100L*1024*1024)throw new RemoteError("Bản mobile hỗ trợ tệp tối đa 100 MB.");
        String id=message.getString("id");if(!id.matches("[A-Fa-f0-9]{32,64}"))throw new IOException();
        File dir=new File(getCacheDir(),"attachments");dir.mkdirs();String name=message.optString("fileName","tep").replaceAll("[^\\p{L}\\p{N}._-]","_");if(name.length()>100)name=name.substring(name.length()-100);
        File file=new File(dir,id+"_"+name),part=new File(dir,id+"."+UUID.randomUUID()+".part");MessageDigest hash=MessageDigest.getInstance("SHA-256");
        try(FileOutputStream out=new FileOutputStream(part)){
            long offset=0;while(offset<size){interactiveCommand(connection);if(background&&(stopping||!visible||!selected.equals(conversation)))throw new RemoteError("Đã dừng tải ảnh nền");JSONObject request=packet("download",conversation).put("messageId",id).put("offset",offset);JSONObject response=checked(connection.request(request));byte[] data=Base64.decode(response.getString("data"),Base64.NO_WRAP);if(data.length==0||data.length>32768||offset+data.length>size)throw new RemoteError("Tệp bị thiếu dữ liệu");out.write(data);hash.update(data);offset+=data.length;}
        }catch(Exception e){part.delete();throw e;}
        if(!DeviceKeys.hex(hash.digest()).equalsIgnoreCase(message.getString("hash"))){part.delete();throw new RemoteError("Ảnh hoặc tệp có checksum không khớp");}
        if(!part.renameTo(file)){part.delete();throw new IOException("Không lưu được tệp");}return file;
    }
    private void autoImage(String conversation)throws Exception{
        JSONArray batch=messages;for(int i=batch.length()-1;i>=0;i--){JSONObject message=batch.getJSONObject(i);String id=message.getString("id");
            if("image".equals(message.optString("kind"))&&message.optLong("fileSize")<=10*1024*1024&&!images.containsKey(id)&&!failedImages.contains(id)&&("received".equals(message.optString("status"))||message.optBoolean("outgoing"))){
                try{File file=new File(getCacheDir(),"preview-"+id);boolean restored=false;if(cache!=null)try{restored=cache.restoreImage(message,file);}catch(Exception ignored){}if(!restored){file=downloadNow(pc,conversation,message,true);if(cache!=null)try{cache.saveImage(message,file);}catch(Exception ignored){}}if(selected.equals(conversation)&&visible){images.put(id,file);notifyUi();}else file.delete();}catch(RemoteError ignored){if(selected.equals(conversation))failedImages.add(id);}break;
            }
        }
        if(!selected.equals(conversation))return;Set<String> active=new HashSet<>();for(int i=0;i<batch.length();i++)active.add(batch.getJSONObject(i).getString("id"));
        for(String key:images.keySet())if(!active.contains(key)){File file=images.remove(key);if(file!=null)file.delete();}
    }
    void upload(Uri uri,String name,Consumer<String> done){String conversation=selected;enqueue(connection->{
        File temp=File.createTempFile("upload-",".tmp",getCacheDir());try{
            MessageDigest digest=MessageDigest.getInstance("SHA-256");long size=0;
            try(InputStream in=getContentResolver().openInputStream(uri);FileOutputStream out=new FileOutputStream(temp)){if(in==null)throw new IOException();byte[] bytes=new byte[32768];int n;while((n=in.read(bytes))>=0){size+=n;if(size>100L*1024*1024)throw new RemoteError("Tệp tối đa 100 MB.");out.write(bytes,0,n);digest.update(bytes,0,n);}}
            checked(connection.request(packet("upload-start",conversation).put("fileName",name).put("size",size).put("hash",DeviceKeys.hex(digest.digest()))));
            try(FileInputStream in=new FileInputStream(temp)){byte[] bytes=new byte[32768];int n;long offset=0;while((n=in.read(bytes))>=0){interactiveCommand(connection);checked(connection.request(packet("upload-chunk",conversation).put("offset",offset).put("data",Base64.encodeToString(bytes,0,n,Base64.NO_WRAP))));offset+=n;}}
            checked(connection.request(packet("upload-finish",conversation).put("fileName",name)));
        }finally{temp.delete();}
    },done,true);}
    static void clearMedia(){for(File file:images.values())file.delete();images.clear();}
    public void onDestroy(){stopping=true;connected=false;PcConnection connection=pc;if(connection!=null)connection.close();if(worker!=null)worker.interrupt();current=null;status="Đã ngắt kết nối · Lịch sử đã lưu trên máy";snapshots.clear();conversations=cache==null?new JSONArray():cache.conversations();clearMedia();restoreSelected();notifyUi();super.onDestroy();}
}
