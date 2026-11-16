package vn.lantern.companion;

import android.content.Context;
import android.util.AtomicFile;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.security.MessageDigest;
import java.util.*;

/** Account-scoped, authenticated encrypted snapshots; never stores passwords or expiring messages. */
final class HistoryCache {
    private static final Object disk=new Object();
    private static long epoch;
    private final long generation;
    private final File directory;
    private final String account;
    HistoryCache(Context context,String account) throws IOException {
        if(!account.matches("[A-F0-9]{64}"))throw new IOException("Danh tính PC không hợp lệ");
        this.account=account;directory=new File(context.getFilesDir(),"history-cache");synchronized(disk){generation=epoch;directory.mkdirs();}
    }
    private String scope(String key){return "Lantern-cache-v1:"+account+":"+key;}
    private File path(String key)throws Exception{return new File(directory,DeviceKeys.hex(MessageDigest.getInstance("SHA-256").digest(scope(key).getBytes(StandardCharsets.UTF_8)))+(key.startsWith("send:")?".receipt":key.equals("conversations")?".index":".enc"));}
    private byte[] read(String key)throws Exception{synchronized(disk){if(generation!=epoch)return null;File file=path(key);if(!file.exists())return null;if(file.length()>12*1024*1024)throw new IOException("Cache quá lớn");byte[] bytes=new AtomicFile(file).readFully();file.setLastModified(System.currentTimeMillis());return DeviceKeys.openCache(bytes,scope(key));}}
    private void write(String key,byte[] bytes)throws Exception{synchronized(disk){if(generation!=epoch)throw new IOException("Cache đã được xóa");directory.mkdirs();AtomicFile file=new AtomicFile(path(key));byte[] encrypted=DeviceKeys.sealCache(bytes,scope(key));FileOutputStream out=null;try{out=file.startWrite();out.write(encrypted);file.finishWrite(out);}catch(Exception e){if(out!=null)file.failWrite(out);throw e;}trim();}}
    private void trim(){File[] files=directory.listFiles((dir,name)->name.endsWith(".enc"));if(files==null)return;Arrays.sort(files,Comparator.comparingLong(File::lastModified));long size=0;for(File f:files)size+=f.length();for(File f:files){if(size<=100L*1024*1024)break;long length=f.length();if(f.delete())size-=length;}}
    void saveConversations(JSONArray conversations)throws Exception{write("conversations",conversations.toString().getBytes(StandardCharsets.UTF_8));}
    JSONArray conversations(){try{byte[] bytes=read("conversations");JSONArray result=bytes==null?new JSONArray():new JSONArray(new String(bytes,StandardCharsets.UTF_8));for(int i=0;i<result.length();i++){JSONObject c=result.getJSONObject(i);c.put("locked",c.optBoolean("protected",true));c.put("presence","offline");}return result;}catch(Exception e){return new JSONArray();}}
    void saveMessages(String conversation,JSONArray messages)throws Exception{JSONArray persistent=new JSONArray();for(int i=0;i<messages.length();i++){JSONObject m=messages.getJSONObject(i);if(m.optInt("disappearAfterSeconds")==0&&m.isNull("expiresAt"))persistent.put(m);}write("messages:"+conversation,persistent.toString().getBytes(StandardCharsets.UTF_8));}
    JSONArray messages(String conversation){try{byte[] bytes=read("messages:"+conversation);return bytes==null?new JSONArray():new JSONArray(new String(bytes,StandardCharsets.UTF_8));}catch(Exception e){return new JSONArray();}}
    void saveImage(JSONObject message,File file)throws Exception{if(message.optInt("disappearAfterSeconds")==0&&message.isNull("expiresAt")&&file.length()<=10L*1024*1024)write("image:"+message.getString("id")+":"+message.getString("hash"),Files.readAllBytes(file.toPath()));}
    boolean restoreImage(JSONObject message,File destination)throws Exception{byte[] bytes=read("image:"+message.getString("id")+":"+message.getString("hash"));if(bytes==null)return false;Files.write(destination.toPath(),bytes);return true;}
    String sendId(String conversation,String text,String reply)throws Exception{synchronized(disk){String fingerprint=DeviceKeys.hex(MessageDigest.getInstance("SHA-256").digest((text+"\u0000"+reply).getBytes(StandardCharsets.UTF_8)));byte[] previous=read("send:"+conversation);if(previous!=null){JSONObject saved=new JSONObject(new String(previous,StandardCharsets.UTF_8));if(fingerprint.equals(saved.optString("fingerprint")))return saved.getString("id");}String id=UUID.randomUUID().toString().replace("-","");write("send:"+conversation,new JSONObject().put("fingerprint",fingerprint).put("id",id).toString().getBytes(StandardCharsets.UTF_8));return id;}}
    void confirmSend(String conversation,String id)throws Exception{synchronized(disk){byte[] previous=read("send:"+conversation);if(previous!=null&&id.equals(new JSONObject(new String(previous,StandardCharsets.UTF_8)).optString("id")))new AtomicFile(path("send:"+conversation)).delete();}}
    static void clearAll(Context context){synchronized(disk){epoch++;File directory=new File(context.getFilesDir(),"history-cache");File[] files=directory.listFiles();if(files!=null)for(File f:files)if(f.isFile())f.delete();}}
}
