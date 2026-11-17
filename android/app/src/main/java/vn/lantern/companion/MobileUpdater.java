package vn.lantern.companion;

import android.content.*;
import android.content.pm.*;
import android.os.*;
import org.json.JSONObject;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.Executors;
import java.util.function.Consumer;

/** Downloads use their own executor; they never queue ahead of chat traffic. */
final class MobileUpdater {
    private static MobileUpdater instance;
    static synchronized MobileUpdater get(Context context) { if(instance==null) instance=new MobileUpdater(context.getApplicationContext()); return instance; }
    final Context context;
    final SharedPreferences prefs;
    private final Handler main=new Handler(Looper.getMainLooper());
    private final java.util.concurrent.ExecutorService worker=Executors.newSingleThreadExecutor();
    private final Set<Runnable> listeners=new HashSet<>();
    private final File apk;
    private boolean restored;
    boolean busy,ready;
    String status="Chưa kiểm tra cập nhật.",sha="";
    ReleaseUpdate release;
    private MobileUpdater(Context context) {
        this.context=context; prefs=context.getSharedPreferences("updates",0);
        File dir=new File(context.getCacheDir(),"updates"); dir.mkdirs(); apk=new File(dir,ReleaseUpdate.ASSET);
    }
    void listen(Runnable listener) { listeners.add(listener); listener.run(); }
    void unlisten(Runnable listener) { listeners.remove(listener); }
    private void emit() { for(Runnable listener:new ArrayList<>(listeners)) listener.run(); }
    private void progress(String value) { main.post(()->{status=value;emit();}); }
    String version() { try { return context.getPackageManager().getPackageInfo(context.getPackageName(),0).versionName; } catch(Exception e) { return "0.0.0"; } }
    boolean autoCheck() { return prefs.getBoolean("autoCheck",true); }
    boolean autoDownload() { return prefs.getBoolean("autoDownload",true); }
    void automatic() { if(autoCheck()) check(false); }
    void check(boolean force) {
        if(busy) return;
        boolean due=force||System.currentTimeMillis()-prefs.getLong("lastAttempt",0)>6*60*60*1000;
        if(!due&&restored) return;
        busy=true; status="Đang kiểm tra bản mới…"; emit();
        worker.execute(()->{
            ReleaseUpdate found=release; String foundSha=sha; boolean cached=ready; String message;
            try {
                if(!restored) {
                    try {
                        ReleaseUpdate old=ReleaseUpdate.parse(new JSONObject(prefs.getString("release","")));
                        if(ReleaseUpdate.code(old.version)>ReleaseUpdate.code(version())) {
                            String checksum=prefs.getString("sha",""); old.verify(apk,checksum); verifyApk(context,apk,old.version); found=old; foundSha=checksum; cached=true;
                        } else { apk.delete(); prefs.edit().remove("release").remove("sha").apply(); }
                    } catch(Exception ignored) { apk.delete(); }
                    restored=true;
                }
                if(due) {
                    prefs.edit().putLong("lastAttempt",System.currentTimeMillis()).apply();
                    JSONObject json=new JSONObject(new String(ReleaseUpdate.fetch(ReleaseUpdate.API,2*1024*1024),StandardCharsets.UTF_8));
                    ReleaseUpdate next=ReleaseUpdate.parse(json);
                    if(ReleaseUpdate.code(next.version)>ReleaseUpdate.code(version())) {
                        String checksum=ReleaseUpdate.checksum(new String(ReleaseUpdate.fetch(next.sumsUrl,16384),StandardCharsets.UTF_8),ReleaseUpdate.ASSET);
                        if(!next.digest.isEmpty()&&!next.digest.equals("sha256:"+checksum)) throw new IOException("Hai checksum của bản phát hành không khớp.");
                        cached=cached&&found!=null&&found.version.equals(next.version)&&foundSha.equals(checksum);
                        found=next; foundSha=checksum; prefs.edit().putString("release",json.toString()).putString("sha",checksum).apply();
                    } else { found=null; foundSha=""; cached=false; apk.delete(); prefs.edit().remove("release").remove("sha").apply(); }
                }
                message=found==null?"Đang dùng phiên bản mới nhất.":cached?"Bản "+found.version+" đã tải · Sẵn sàng cài đặt.":"Có phiên bản "+found.version+".";
            } catch(Exception e) { message=(cached?"Bản tải sẵn vẫn có thể cài. ":"")+error(e); }
            ReleaseUpdate result=found; String hash=foundSha,text=message; boolean downloaded=cached;
            main.post(()->{release=result;sha=hash;ready=downloaded;busy=false;status=text;emit();if(release!=null&&!ready&&autoDownload())download();});
        });
    }
    void download() {
        if(busy||release==null) return;
        ReleaseUpdate selected=release; String hash=sha; busy=true;ready=false;status="Đang tải bản "+selected.version+"…";emit();
        worker.execute(()->{
            try { selected.download(apk,hash,p->progress("Đang tải bản "+selected.version+" · "+p+"%")); verifyApk(context,apk,selected.version);
                main.post(()->{busy=false;ready=true;status="Bản "+selected.version+" đã tải · Sẵn sàng cài đặt.";emit();});
            } catch(Exception e) { apk.delete();main.post(()->{busy=false;ready=false;status=error(e);emit();}); }
        });
    }
    void prepareInstall(Consumer<File> done) {
        if(busy||!ready||release==null) return;
        ReleaseUpdate selected=release;String hash=sha;busy=true;status="Đang xác minh APK trước khi cài…";emit();
        worker.execute(()->{
            try { selected.verify(apk,hash);verifyApk(context,apk,selected.version); main.post(()->{busy=false;status="Xác nhận cập nhật trong trình cài đặt Android.";emit();done.accept(apk);}); }
            catch(Exception e) { apk.delete();main.post(()->{busy=false;ready=false;status=error(e);emit();}); }
        });
    }
    void installationResult(boolean success) { status=success?"Đã cài bản cập nhật.":"Chưa cài cập nhật. Có thể bấm Cài đặt để thử lại.";emit(); }
    void problem(String message) { status=message;emit(); }
    @SuppressWarnings("deprecation")
    static void verifyApk(Context context,File file,String version) throws Exception {
        PackageManager pm=context.getPackageManager();int flags=Build.VERSION.SDK_INT>=28?PackageManager.GET_SIGNING_CERTIFICATES:PackageManager.GET_SIGNATURES;
        PackageInfo installed=pm.getPackageInfo(context.getPackageName(),flags),candidate=pm.getPackageArchiveInfo(file.getAbsolutePath(),flags);
        long current=Build.VERSION.SDK_INT>=28?installed.getLongVersionCode():installed.versionCode;
        long next=candidate==null?0:Build.VERSION.SDK_INT>=28?candidate.getLongVersionCode():candidate.versionCode;
        if(candidate==null||!context.getPackageName().equals(candidate.packageName)||!version.equals(candidate.versionName)||next!=ReleaseUpdate.code(version)||next<=current) throw new IOException("APK không đúng ứng dụng hoặc không mới hơn bản đang dùng.");
        if(candidate.applicationInfo==null||candidate.applicationInfo.minSdkVersion>Build.VERSION.SDK_INT) throw new IOException("Bản mới chưa hỗ trợ phiên bản Android này.");
        Signature[] a=Build.VERSION.SDK_INT>=28&&installed.signingInfo!=null?installed.signingInfo.getApkContentsSigners():installed.signatures;
        Signature[] b=Build.VERSION.SDK_INT>=28&&candidate.signingInfo!=null?candidate.signingInfo.getApkContentsSigners():candidate.signatures;
        if(!sameSigners(a,b)) throw new IOException("Khóa ký APK khác bản đang cài. Không thể cập nhật đè an toàn.");
    }
    static boolean sameSigners(Signature[] a,Signature[] b) { return a!=null&&b!=null&&a.length>0&&a.length==b.length&&new HashSet<>(Arrays.asList(a)).equals(new HashSet<>(Arrays.asList(b))); }
    private static String error(Exception e) { return e instanceof IOException&&e.getMessage()!=null?e.getMessage():"Không kiểm tra/tải được cập nhật. Kiểm tra Internet rồi thử lại."; }
}
