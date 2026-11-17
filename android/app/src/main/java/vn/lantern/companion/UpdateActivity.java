package vn.lantern.companion;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.provider.Settings;
import android.widget.*;
import androidx.core.content.FileProvider;

/** An explicit install never loses its pending APK while visiting Unknown apps settings. */
public final class UpdateActivity extends Activity {
    private MobileUpdater updater;
    private TextView status;
    private Button check,download,install;
    private boolean waitingForPermission;
    private final Runnable changed=this::render;
    public void onCreate(Bundle state) {
        super.onCreate(state);setTitle("Cập nhật Lantern");updater=MobileUpdater.get(this);
        waitingForPermission=state!=null&&state.getBoolean("permission");
        int pad=(int)(24*getResources().getDisplayMetrics().density);
        ScrollView scroll=new ScrollView(this);LinearLayout panel=new LinearLayout(this);panel.setOrientation(LinearLayout.VERTICAL);panel.setPadding(pad,pad,pad,pad);scroll.addView(panel);setContentView(scroll);
        if(android.os.Build.VERSION.SDK_INT>=30)scroll.setOnApplyWindowInsetsListener((view,insets)->{android.graphics.Insets bars=insets.getInsets(android.view.WindowInsets.Type.systemBars());scroll.setPadding(bars.left,bars.top,bars.right,bars.bottom);return insets;});
        TextView heading=new TextView(this);heading.setText("Cập nhật ứng dụng");heading.setTextSize(26);panel.addView(heading);
        TextView version=new TextView(this);version.setText("Phiên bản hiện tại: "+updater.version());version.setPadding(0,pad/2,0,pad);panel.addView(version);
        Switch auto=new Switch(this);auto.setText("Tự kiểm tra bản mới");auto.setChecked(updater.autoCheck());panel.addView(auto);
        Switch load=new Switch(this);load.setText("Tự tải bản cập nhật");load.setChecked(updater.autoDownload());panel.addView(load);
        auto.setOnCheckedChangeListener((b,value)->{updater.prefs.edit().putBoolean("autoCheck",value).apply();if(value)updater.automatic();});
        load.setOnCheckedChangeListener((b,value)->{updater.prefs.edit().putBoolean("autoDownload",value).apply();if(value&&updater.release!=null&&!updater.ready)updater.download();});
        TextView hint=new TextView(this);hint.setText("Kiểm tra mỗi 6 giờ khi mở app. Tải bằng kết nối Internet hiện tại, kể cả dữ liệu di động. Bản tải xong chờ bạn bấm Cài đặt và xác nhận với Android; cài đè giữ ghép đôi và lịch sử.");hint.setPadding(0,pad/2,0,pad);panel.addView(hint);
        status=new TextView(this);status.setTextSize(16);panel.addView(status);
        check=button(panel,"Kiểm tra ngay",()->updater.check(true));
        download=button(panel,"Tải bản mới",()->updater.download());
        install=button(panel,"Cài đặt bản đã tải",this::install);
        button(panel,"Xem bản phát hành",()->startActivity(new Intent(Intent.ACTION_VIEW,Uri.parse("https://github.com/zeusato/Lantern/releases/latest"))));
        button(panel,"Quay lại",this::finish);
        if(updater.release==null)updater.check(false);
    }
    private Button button(LinearLayout panel,String title,Runnable action) { Button b=new Button(this);b.setText(title);b.setAllCaps(false);b.setOnClickListener(v->action.run());panel.addView(b);return b; }
    protected void onResume() { super.onResume();updater.listen(changed); }
    protected void onPause() { updater.unlisten(changed);super.onPause(); }
    protected void onSaveInstanceState(Bundle state) { state.putBoolean("permission",waitingForPermission);super.onSaveInstanceState(state); }
    private void render() { if(status==null)return;status.setText(updater.status);check.setEnabled(!updater.busy);download.setEnabled(!updater.busy&&updater.release!=null&&!updater.ready);install.setEnabled(!updater.busy&&updater.ready); }
    @SuppressWarnings("deprecation")
    private void install() {
        if(!getPackageManager().canRequestPackageInstalls()) {
            waitingForPermission=true;
            try { startActivityForResult(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,Uri.parse("package:"+getPackageName())),71); }
            catch(Exception e) { waitingForPermission=false;updater.problem("Mở Cài đặt Android → Cài ứng dụng không rõ nguồn gốc → cho phép Lantern, rồi thử lại."); }
            return;
        }
        updater.prepareInstall(file->{
            if(isFinishing()||isDestroyed())return;
            try { Uri uri=FileProvider.getUriForFile(this,getPackageName()+".files",file);
                startActivityForResult(new Intent(Intent.ACTION_INSTALL_PACKAGE).setDataAndType(uri,"application/vnd.android.package-archive").addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION).putExtra(Intent.EXTRA_RETURN_RESULT,true),72);
            } catch(Exception e) { updater.problem("Không mở được trình cài đặt Android. Hãy thử lại."); }
        });
    }
    protected void onActivityResult(int request,int result,Intent data) {
        super.onActivityResult(request,result,data);
        if(request==71&&waitingForPermission) { waitingForPermission=false;if(getPackageManager().canRequestPackageInstalls())install();else updater.problem("Chưa cấp quyền cài cập nhật cho Lantern. Bản tải về vẫn được giữ."); }
        if(request==72)updater.installationResult(result==RESULT_OK);
    }
}
