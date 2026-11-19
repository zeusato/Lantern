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
    private final Map<String,String> historyHashes=new ConcurrentHashMap<>();
    private final Set<String> failedImages=new HashSet<>();
    private String savedContacts="";
    private final Object stateGate=new Object();
    private final LinkedHashMap<String,JSONObject> outbox=new LinkedHashMap<>();
    private final ExecutorService disk=Executors.newSingleThreadExecutor();
    private final ExecutorService commands=Executors.newSingleThreadExecutor();
    private long lastSync;
    private long lastSerial;
    private String lastRead="";
    private String phoneId="";
    private final Handler ui=new Handler(Looper.getMainLooper());
    interface Job { void run(PcConnection connection) throws Exception; }
    PcConnection controlConnection() { return AppSession.unlocked() && connected && pc!=null && pc.pcCommands ? pc : null; }
    boolean conversationSettingsSupported(){return connected&&pc!=null&&pc.conversationSettings;}
    JSONObject controlRequest(PcConnection expected,JSONObject command)throws Exception{
        if(!AppSession.unlocked()||stopping||!connected||expected==null||pc!=expected)throw new IOException("Kết nối PC đã thay đổi. Mở lại mục Máy tính.");
        return checked(expected.request(new JSONObject().put("type","pc").put("pc",command))).getJSONObject("pc");
    }
    private record Work(Job job,Consumer<String> done,boolean transfer,boolean login){Work(Job job,Consumer<String> done,boolean transfer){this(job,done,transfer,false);}}
    static JSONObject packet(String type,String conversation) throws JSONException {return new JSONObject().put("type",type).put("conversation",conversation);}
    boolean accountLoginSupported(){return connected&&pc!=null&&pc.appPassword;}
    void loginAccount(String password,Consumer<String> done){
        long generation=AppSession.generation();
        enqueue(connection->{
            if(!connection.appPassword)throw new RemoteError("Cập nhật Lantern trên PC để đăng nhập bằng mật khẩu tài khoản.");
            JSONObject request=packet("app-login","").put("password",password);
            JSONObject response;try{response=checked(connection.request(request));}finally{request.remove("password");}
            if(!"app-authenticated".equals(response.optString("type")))throw new RemoteError("PC chưa xác nhận đăng nhập.");
            if(!AppSession.authenticate(generation)){connection.close();throw new RemoteError("Phiên đăng nhập đã đóng. Đại ca đăng nhập lại.");}
            account=response.optString("name",account);lastSync=0;sync(connection);notifyUi();
        },done,false,true);
    }
    void lockAccount(){
        visible=false;displayed=new ReadState("",Collections.emptySet());
        synchronized(stateGate){conversations=new JSONArray();messages=new JSONArray();clearMedia();}
        PcConnection active=pc;if(active!=null)active.close();notifyUi();
    }
    private void notifyUi(){ui.post(()->{if(changed!=null)changed.run();});}
    public IBinder onBind(Intent intent){return null;}
    public void onCreate(){super.onCreate();AppSession.invalidate();current=this;clearMedia();try{JSONObject config=DeviceKeys.load(this);if(config!=null){phoneId=DeviceKeys.id();cache=new HistoryCache(this,config.getString("pc"));JSONArray saved=cache.outbox();for(int i=0;i<saved.length();i++){JSONObject m=saved.getJSONObject(i);m.put("status","retry");outbox.put(m.getString("id"),m);}conversations=new JSONArray();messages=new JSONArray();}}catch(Exception ignored){}}
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
                config.put("nonce","");DeviceKeys.save(this,config);account=config.optString("name","Lantern");connected=true;problem="";status="Đã kết nối qua "+pc.transport()+" · "+account;lastSerial=0;lastSync=0;lastRead="";
                PcConnection active=pc;active.onPush=response->{if(stopping||pc!=active||!connected||!AppSession.unlocked())return;try{applySync(response.optString("conversation"),response);}catch(Exception ignored){active.close();}};notifyUi();
                active.onDisconnect=()->{if(stopping||pc!=active)return;AppSession.invalidate();connected=false;problem="Kết nối bị gián đoạn";status="Đang kết nối lại · "+problem;synchronized(stateGate){conversations=new JSONArray();messages=new JSONArray();clearMedia();}work.offer(new Work(connection->{},ignored->{},false));notifyUi();};
                long wait=0;
                while(!stopping){
                    Work task=work.poll(wait,TimeUnit.MILLISECONDS);boolean requested=task!=null;if(task!=null)perform(task,pc);
                    while((task=work.poll())!=null)perform(task,pc);
                    if(!pc.pushSync||requested||android.os.SystemClock.elapsedRealtime()-lastSync>=10000||!lastRead.equals(visibleIds(visible?selected:"").toString()))sync(pc);
                    if(visible&&!selected.isEmpty()&&!selectedLocked())autoImage(selected);
                    wait=visible?350:1500;
                }
            }catch(Exception error){if(stopping)break;AppSession.invalidate();connected=false;problem=connectionError(error);status="Đang kết nối lại · "+problem;historyHashes.clear();conversations=new JSONArray();messages=new JSONArray();clearMedia();notifyUi();}
            finally{if(current==this)connected=false;PcConnection connection=pc;pc=null;if(connection!=null)connection.close();Work task;while((task=work.poll())!=null){Work dropped=task;ui.post(()->dropped.done.accept("Mất kết nối. Thao tác này chưa được gửi tới PC."));}}
            if(!stopping)try{Thread.sleep(1000);}catch(InterruptedException ignored){}
        }
        if(!stopping)stopSelf();
    }
    private JSONArray visibleIds(String conversation){JSONArray read=new JSONArray();ReadState shown=displayed;JSONArray batch=messages;if(visible&&shown.conversation().equals(conversation))for(int i=0;i<batch.length();i++){JSONObject m=batch.optJSONObject(i);if(m!=null&&!m.has("localRequest")&&shown.ids().contains(m.optString("id"))&&(!"image".equals(m.optString("kind"))||images.containsKey(m.optString("id"))))read.put(m.optString("id"));}return read;}
    private void sync(PcConnection connection)throws Exception{
        if(!AppSession.unlocked()){connection.request(packet("app-status",""));lastSync=android.os.SystemClock.elapsedRealtime();return;}
        String conversation=visible?selected:"";JSONArray read=visibleIds(conversation);
        JSONObject response=checked(connection.request(packet(connection.fastSync?"sync":"list",connection.fastSync?conversation:"").put("hash",historyHashes.getOrDefault(conversation,"")).put("visibleIds",read)));
        if(connection.fastSync){applySync(conversation,response);lastSync=android.os.SystemClock.elapsedRealtime();lastRead=read.toString();return;}
        applyContacts(response);
        if(!conversation.isEmpty()&&selected.equals(conversation)&&!selectedLocked()){
            if(!connection.fastSync)response=checked(connection.request(packet("history",conversation).put("hash",historyHashes.getOrDefault(conversation,""))));
            applyHistory(conversation,response);
            if(!connection.fastSync&&read.length()>0)checked(connection.request(packet("read",conversation).put("visibleIds",read)));
        }else if(selectedLocked()){messages=new JSONArray();clearMedia();}
        lastSync=android.os.SystemClock.elapsedRealtime();notifyUi();
    }
    private void applyContacts(JSONObject response){synchronized(stateGate){if(stopping)return;JSONArray contacts=response.optJSONArray("conversations");if(contacts!=null){
        Set<String> remaining=new HashSet<>();for(int i=0;i<contacts.length();i++){JSONObject c=contacts.optJSONObject(i);if(c!=null)remaining.add(c.optString("id"));}
        for(int i=0;i<conversations.length();i++){JSONObject c=conversations.optJSONObject(i);if(c==null)continue;String id=c.optString("id");if(remaining.contains(id))continue;
            JSONArray previous=snapshots.remove(id);if(previous==null&&cache!=null)previous=cache.messages(id);clearCachedFiles(previous);historyHashes.remove(id);
            outbox.values().removeIf(m->id.equals(m.optString("conversationId")));try{persistOutbox();}catch(Exception ignored){}
            if(id.equals(selected)){messages=new JSONArray();clearMedia();}
            if(cache!=null)save(()->{try{cache.clearConversation(id);}catch(Exception ignored){}});
        }
        for(int i=0;i<contacts.length();i++){JSONObject c=contacts.optJSONObject(i);if(c==null)continue;String id=c.optString("id");if(c.optLong("historyVersion")!=historyVersion(id)){
            JSONArray previous=snapshots.get(id);if(previous==null&&cache!=null)previous=cache.messages(id);clearCachedFiles(previous);
            snapshots.put(id,new JSONArray());historyHashes.remove(id);if(id.equals(selected))clearMedia();
            if(cache!=null)save(()->{try{cache.clearConversation(id);}catch(Exception ignored){}});
        }}
        conversations=contacts;String signature=contacts.toString();if(cache!=null&&!signature.equals(savedContacts)){savedContacts=signature;save(()->{try{cache.saveConversations(contacts);}catch(Exception ignored){}});}
    }}}
    private long historyVersion(String conversation){for(int i=0;i<conversations.length();i++){JSONObject c=conversations.optJSONObject(i);if(c!=null&&conversation.equals(c.optString("id")))return c.optLong("historyVersion");}return 0;}
    private void clearCachedFiles(JSONArray previous){
        if(previous==null)return;Set<String> ids=new HashSet<>();for(int i=0;i<previous.length();i++){JSONObject m=previous.optJSONObject(i);if(m!=null&&m.optString("id").matches("[A-Fa-f0-9]{32,64}")){String id=m.optString("id");ids.add(id);new File(getCacheDir(),"preview-"+id).delete();}}
        File[] files=new File(getCacheDir(),"attachments").listFiles();if(files!=null)for(File file:files)for(String id:ids)if(file.getName().startsWith(id+"_")||file.getName().startsWith(id+".")){file.delete();break;}
    }
    private void applySync(String conversation,JSONObject response)throws Exception{synchronized(stateGate){if(stopping||!AppSession.unlocked())return;if("app-locked".equals(response.optString("type"))){AppSession.invalidate();conversations=new JSONArray();messages=new JSONArray();notifyUi();return;}long serial=response.optLong("serial");if(serial>0&&serial<=lastSerial)return;lastSerial=serial;applyContacts(response);if(!conversation.isEmpty()&&!response.optBoolean("locked",true)&&response.has("messages"))applyHistory(conversation,response);else restoreSelected();if(selectedLocked())clearMedia();}notifyUi();}
    private void save(Runnable task){if(!stopping)try{disk.execute(task);}catch(RejectedExecutionException ignored){}}
    private void applyHistory(String conversation,JSONObject history)throws Exception{
        synchronized(stateGate){if(stopping)return;if(!"unchanged".equals(history.optString("type"))){JSONArray batch=history.getJSONArray("messages");snapshots.put(conversation,batch);historyHashes.put(conversation,history.optString("hash"));boolean reconciled=false;for(int i=0;i<batch.length();i++)if(outbox.remove(batch.getJSONObject(i).getString("id"))!=null)reconciled=true;persistMessages(conversation,batch);if(reconciled)persistOutbox();}if(selected.equals(conversation))restoreSelected();}
    }
    private void persistMessages(String conversation,JSONArray batch){if(cache!=null)save(()->{try{cache.saveMessages(conversation,batch);}catch(Exception ignored){}});}
    private JSONArray pendingSnapshot()throws Exception{JSONArray result=new JSONArray();for(JSONObject m:outbox.values())result.put(new JSONObject(m.toString()));return result;}
    private void persistOutbox()throws Exception{JSONArray saved=pendingSnapshot();if(cache!=null)save(()->{try{cache.saveOutbox(saved);}catch(Exception ignored){}});}
    String sendText(String conversation,String text,String reply){
        synchronized(stateGate){try{
            if(stopping||cache==null||phoneId.isEmpty())return "Chưa sẵn sàng lưu tin trên thiết bị.";
            if(!conversation.equals(selected)||selectedLocked())return "Hãy mở khóa hội thoại trước.";
            if(text.length()>16000)return "Tin nhắn tối đa 16.000 ký tự.";
            if(outbox.size()>=100)return "Có quá nhiều tin đang chờ. Hãy xử lý các tin gửi lỗi trước.";
            String request=UUID.randomUUID().toString().replace("-","");String id=DeviceKeys.hex(MessageDigest.getInstance("SHA-256").digest((phoneId+request).getBytes(java.nio.charset.StandardCharsets.UTF_8))).substring(0,32).toLowerCase(Locale.ROOT);
            JSONObject message=new JSONObject().put("id",id).put("localRequest",request).put("conversationId",conversation).put("text",text).put("replyToId",reply).put("kind","text").put("outgoing",true).put("senderName",account).put("time",java.time.OffsetDateTime.now().toString()).put("status","sending");
            for(int i=0;i<conversations.length();i++){JSONObject c=conversations.optJSONObject(i);if(c!=null&&conversation.equals(c.optString("id")))message.put("disappearAfterSeconds",c.optInt("disappearSeconds"));}
            outbox.put(id,message);restoreSelected();notifyUi();scheduleSend(id);return null;
        }catch(Exception error){return "Không tạo được tin nhắn. Nội dung vẫn được giữ.";}}
    }
    void retrySend(String id){synchronized(stateGate){JSONObject m=outbox.get(id);if(m==null||!"retry".equals(m.optString("status"))||selectedLocked())return;try{m.put("status","sending");restoreSelected();notifyUi();scheduleSend(id);}catch(Exception ignored){}}}
    private void scheduleSend(String id)throws Exception{
        JSONArray saved=pendingSnapshot();JSONObject message=new JSONObject(outbox.get(id).toString());
        disk.execute(()->{try{cache.saveOutbox(saved);if(stopping)return;
            JSONObject request=packet("send",message.getString("conversationId")).put("id",message.getString("localRequest")).put("text",message.getString("text")).put("replyTo",message.optString("replyToId"));
            enqueue(connection->{synchronized(stateGate){if(stopping||!outbox.containsKey(id))return;}JSONObject response=checked(connection.request(request));synchronized(stateGate){if(stopping||!outbox.containsKey(id))return;lastSerial=Math.max(lastSerial,response.optLong("serial"));if("gone".equals(response.optString("type"))){outbox.remove(id);String target=message.getString("conversationId");JSONArray prior=snapshots.getOrDefault(target,new JSONArray()),remaining=new JSONArray();for(int i=0;i<prior.length();i++)if(!id.equals(prior.getJSONObject(i).optString("id")))remaining.put(prior.getJSONObject(i));snapshots.put(target,remaining);persistMessages(target,remaining);persistOutbox();restoreSelected();notifyUi();return;}JSONObject sent=null;JSONArray received=response.optJSONArray("messages");if(received!=null&&received.length()>0)sent=received.getJSONObject(0);if(sent==null){sent=new JSONObject(message.toString());sent.remove("localRequest");sent.put("status","queued");}String conversation=message.getString("conversationId");JSONArray old=snapshots.get(conversation);if(old==null)old=cache.messages(conversation);JSONArray next=new JSONArray();for(int i=0;i<old.length();i++)if(!id.equals(old.getJSONObject(i).optString("id")))next.put(old.getJSONObject(i));next.put(sent);snapshots.put(conversation,next);outbox.remove(id);persistMessages(conversation,next);persistOutbox();restoreSelected();}notifyUi();},error->{if(error!=null)sendFailed(id,error);});
        }catch(Exception e){sendFailed(id,"Không lưu được tin để gửi. Chạm để thử lại.");}});
    }
    private void sendFailed(String id,String error){if(stopping)return;synchronized(stateGate){JSONObject m=outbox.get(id);if(m==null)return;try{m.put("status","retry").put("localError",error);persistOutbox();}catch(Exception ignored){}restoreSelected();}notifyUi();}
    private static final class RemoteError extends Exception{RemoteError(String message){super(message);}}
    private static String connectionError(Exception error){for(Throwable e=error;e!=null;e=e.getCause()){if(e instanceof java.net.SocketTimeoutException)return "PC phản hồi quá chậm";if(e instanceof java.net.UnknownHostException)return "Không tìm thấy địa chỉ relay";if(e instanceof javax.net.ssl.SSLHandshakeException)return "Không xác thực được kết nối với PC";if(e instanceof java.net.ConnectException)return "Chưa tới được cổng kết nối PC";if(e instanceof java.io.EOFException)return "PC đã đóng kết nối";}return error instanceof RemoteError?error.getMessage():"Kết nối bị gián đoạn";}
    private void perform(Work task,PcConnection connection)throws Exception{try{if(!task.login&&!AppSession.unlocked())throw new RemoteError("Đăng nhập tài khoản trước khi tiếp tục.");task.job.run(connection);ui.post(()->task.done.accept(null));}catch(Exception e){ui.post(()->task.done.accept(e instanceof RemoteError?e.getMessage():"Chưa nhận được xác nhận từ PC. Thử lại sẽ dùng cùng mã gửi để tránh trùng."));if(!(e instanceof RemoteError))throw e;}}
    private static JSONObject checked(JSONObject response)throws Exception{if("error".equals(response.optString("type")))throw new RemoteError(response.optString("text","PC từ chối thao tác"));return response;}
    void submit(JSONObject request,Consumer<String> done){enqueue(connection->{
        JSONObject response=checked(connection.request(request));String type=request.optString("type"),conversation=request.optString("conversation");
        if("unlock".equals(type)||"lock".equals(type)){synchronized(stateGate){if(stopping)return;long serial=response.optLong("serial");if(serial>0&&serial<lastSerial)return;lastSerial=Math.max(lastSerial,serial);JSONArray copy=new JSONArray(conversations.toString());for(int i=0;i<copy.length();i++)if(conversation.equals(copy.getJSONObject(i).optString("id")))copy.getJSONObject(i).put("locked","lock".equals(type));conversations=copy;if("unlock".equals(type)&&response.has("hash")&&!response.optString("hash").isEmpty())applyHistory(conversation,response);else{if(conversation.equals(selected))clearMedia();restoreSelected();}}notifyUi();}
        else if(Set.of("conversation-rename","conversation-timer","delete-messages","clear-history").contains(type)){
            boolean deleting=type.equals("delete-messages")||type.equals("clear-history");
            if(deleting)synchronized(stateGate){Set<String> ids=new HashSet<>();JSONArray requested=request.optJSONArray("messageIds");if(requested!=null)for(int i=0;i<requested.length();i++)ids.add(requested.optString(i));outbox.values().removeIf(m->conversation.equals(m.optString("conversationId"))&&(type.equals("clear-history")||ids.contains(m.optString("id"))));persistOutbox();}
            JSONArray contacts=response.optJSONArray("conversations");
            if(contacts!=null&&contacts.length()>0)applySync(conversation,response);
            else if(response.has("hash")){applyHistory(conversation,response);lastSync=0;}
            restoreSelected();notifyUi();
        }
    },done);}
    private void enqueue(Job task,Consumer<String> done){enqueue(task,done,false);}
    private void enqueue(Job task,Consumer<String> done,boolean transfer){enqueue(task,done,transfer,false);}
    private void enqueue(Job task,Consumer<String> done,boolean transfer,boolean login){if(!login&&!AppSession.unlocked()){ui.post(()->done.accept("Đăng nhập tài khoản trước khi tiếp tục."));return;}PcConnection active=pc;Work command=new Work(task,done,transfer,login);if(stopping||!connected||active==null)ui.post(()->done.accept("Đang nối lại PC. Chạm tin nhắn để thử lại khi có kết nối."));else if(!transfer&&active.pushSync){try{commands.execute(()->{if(stopping||pc!=active){ui.post(()->done.accept("Kết nối đã thay đổi. Chạm để thử lại."));return;}try{perform(command,active);}catch(Exception e){active.close();}});}catch(RejectedExecutionException e){ui.post(()->done.accept("Đã ngắt kết nối."));}}else if(!work.offer(command))ui.post(()->done.accept("Đang xử lý các thao tác trước. Đại ca đợi một chút."));}
    private void interactiveCommand(PcConnection connection)throws Exception{for(Work command:work)if(!command.transfer&&work.remove(command))perform(command,connection);if(android.os.SystemClock.elapsedRealtime()-lastSync>=(connection.pushSync?10000:500))sync(connection);}
    void select(String conversation){if(!conversation.equals(selected)){selected=conversation;clearMedia();restoreSelected();restoreCachedImages(conversation,messages);}if(connected)work.offer(new Work(connection->{},ignored->{},false));notifyUi();}
    private void restoreCachedImages(String conversation,JSONArray batch){if(cache==null||selectedLocked())return;long revision=historyVersion(conversation);Thread loader=new Thread(()->{for(int i=0;i<batch.length()&&!stopping&&conversation.equals(selected)&&!selectedLocked();i++){JSONObject message=batch.optJSONObject(i);if(message==null||!"image".equals(message.optString("kind")))continue;File file=new File(getCacheDir(),"preview-"+message.optString("id"));try{if(cache.restoreImage(message,file)){if(!stopping&&current==this&&revision==historyVersion(conversation)&&conversation.equals(selected)&&!selectedLocked()){images.put(message.getString("id"),file);notifyUi();}else file.delete();}}catch(Exception ignored){}}},"lantern-cache");loader.setDaemon(true);loader.start();}
    private boolean selectedLocked(){if(!AppSession.unlocked())return true;for(int i=0;i<conversations.length();i++){JSONObject c=conversations.optJSONObject(i);if(c!=null&&selected.equals(c.optString("id")))return c.optBoolean("locked",true);}return true;}
    private void restoreSelected(){synchronized(stateGate){if(selectedLocked()){messages=new JSONArray();return;}JSONArray base=snapshots.get(selected);if(base==null){base=cache==null?new JSONArray():cache.messages(selected);snapshots.put(selected,base);}JSONArray combined=new JSONArray();Set<String> ids=new HashSet<>();for(int i=0;i<base.length();i++){JSONObject m=base.optJSONObject(i);if(m!=null){combined.put(m);ids.add(m.optString("id"));}}for(JSONObject m:outbox.values())if(selected.equals(m.optString("conversationId"))&&!ids.contains(m.optString("id")))combined.put(m);messages=combined;}}
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
        long revision=historyVersion(conversation);JSONArray batch=messages;for(int i=batch.length()-1;i>=0;i--){JSONObject message=batch.getJSONObject(i);String id=message.getString("id");
            if("image".equals(message.optString("kind"))&&message.optLong("fileSize")<=10*1024*1024&&!images.containsKey(id)&&!failedImages.contains(id)&&("received".equals(message.optString("status"))||message.optBoolean("outgoing"))){
                try{File file=new File(getCacheDir(),"preview-"+id);boolean restored=false;if(cache!=null)try{restored=cache.restoreImage(message,file);}catch(Exception ignored){}if(!restored){file=downloadNow(pc,conversation,message,true);if(revision!=historyVersion(conversation)){file.delete();return;}if(cache!=null)try{cache.saveImage(message,file);}catch(Exception ignored){}}if(!stopping&&current==this&&revision==historyVersion(conversation)&&selected.equals(conversation)&&visible&&!selectedLocked()){images.put(id,file);notifyUi();}else file.delete();}catch(RemoteError ignored){if(selected.equals(conversation))failedImages.add(id);}break;
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
    public void onDestroy(){
        AppSession.invalidate();
        synchronized(stateGate){stopping=true;connected=false;status="Đã ngắt kết nối · Lịch sử đã lưu trên máy";conversations=new JSONArray();messages=new JSONArray();clearMedia();}
        PcConnection connection=pc;if(connection!=null)connection.close();if(worker!=null)worker.interrupt();
        // Publish service shutdown only after the protected view has been locked.
        if(current==this)current=null;notifyUi();disk.shutdown();commands.shutdown();super.onDestroy();
    }
}
