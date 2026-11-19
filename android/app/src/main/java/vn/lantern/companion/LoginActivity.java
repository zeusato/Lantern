package vn.lantern.companion;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Color;
import android.os.Bundle;
import android.text.InputType;
import android.view.Gravity;
import android.view.View;
import android.view.WindowManager;
import android.view.inputmethod.EditorInfo;
import android.widget.*;
import androidx.core.view.ViewCompat;
import androidx.core.view.WindowCompat;
import androidx.core.view.WindowInsetsCompat;

public final class LoginActivity extends Activity {
    private EditText password;private TextView status;private Button submit;private boolean active,busy;
    private final Runnable changed=this::refresh;
    private int dp(int value){return Math.round(value*getResources().getDisplayMetrics().density);}
    @Override public void onCreate(Bundle state){
        super.onCreate(state);getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);WindowCompat.setDecorFitsSystemWindows(getWindow(),false);
        if(!AppSession.paired(this)){openChat();return;}
        LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);root.setGravity(Gravity.CENTER_VERTICAL);root.setPadding(dp(28),dp(24),dp(28),dp(24));root.setBackgroundColor(Color.rgb(16,26,33));
        TextView title=label("Đăng nhập Lantern",27);title.setTypeface(null,android.graphics.Typeface.BOLD);root.addView(title);
        root.addView(label("Nhập mật khẩu tài khoản Lantern trên PC đã ghép đôi để mở ứng dụng.",16));
        password=new EditText(this);password.setContentDescription("Mật khẩu tài khoản Lantern");password.setHint("Mật khẩu tài khoản trên PC");password.setTextColor(Color.WHITE);password.setHintTextColor(Color.rgb(153,174,186));password.setSingleLine();password.setInputType(InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_VARIATION_PASSWORD);password.setImeOptions(EditorInfo.IME_ACTION_GO);password.setSaveEnabled(false);password.setImportantForAutofill(View.IMPORTANT_FOR_AUTOFILL_NO);password.setFilters(new android.text.InputFilter[]{new android.text.InputFilter.LengthFilter(256)});root.addView(password);
        submit=new Button(this);submit.setText("Đăng nhập");submit.setAllCaps(false);submit.setOnClickListener(v->login());root.addView(submit);
        status=label("Đang kết nối PC…",14);status.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);root.addView(status);
        root.addView(label("PC cần đang mở Lantern và có kết nối. Mật khẩu không được lưu trên điện thoại.",13));
        Button update=new Button(this);update.setText("Cập nhật ứng dụng");update.setAllCaps(false);update.setOnClickListener(v->startActivity(new Intent(this,UpdateActivity.class)));root.addView(update);
        Button pair=new Button(this);pair.setText("Đổi PC đã ghép đôi");pair.setAllCaps(false);pair.setOnClickListener(v->new android.app.AlertDialog.Builder(this).setTitle("Đổi PC đã ghép đôi?").setMessage("Xóa ghép đôi và lịch sử đã lưu trên điện thoại để quét QR khác. Dữ liệu trên PC vẫn giữ nguyên.").setNegativeButton("Hủy",null).setPositiveButton("Đổi PC",(dialog,which)->{AppSession.lock();stopService(new Intent(this,ConnectionService.class));DeviceKeys.forget(this);openChat();}).show());root.addView(pair);
        ScrollView scroll=new ScrollView(this);scroll.setFillViewport(true);scroll.setBackgroundColor(Color.rgb(16,26,33));scroll.addView(root);setContentView(scroll);
        ViewCompat.setOnApplyWindowInsetsListener(scroll,(v,insets)->{androidx.core.graphics.Insets bars=insets.getInsets(WindowInsetsCompat.Type.systemBars()|WindowInsetsCompat.Type.displayCutout());v.setPadding(bars.left,bars.top,bars.right,Math.max(bars.bottom,insets.getInsets(WindowInsetsCompat.Type.ime()).bottom));return insets;});ViewCompat.requestApplyInsets(scroll);
        password.setOnEditorActionListener((v,action,event)->{if(action==EditorInfo.IME_ACTION_GO){login();return true;}return false;});password.requestFocus();
    }
    private TextView label(String value,int size){TextView view=new TextView(this);view.setText(value);view.setTextSize(size);view.setTextColor(Color.rgb(234,243,246));view.setPadding(0,dp(8),0,dp(12));return view;}
    @Override protected void onResume(){super.onResume();active=true;if(!AppSession.paired(this)||AppSession.unlocked()){openChat();return;}ConnectionService.visible=false;ConnectionService.changed=changed;if(ConnectionService.current==null)startForegroundService(new Intent(this,ConnectionService.class));refresh();}
    @Override protected void onPause(){active=false;if(password!=null)password.setText("");if(ConnectionService.changed==changed)ConnectionService.changed=null;super.onPause();}
    private void refresh(){if(!active||status==null||busy)return;ConnectionService service=ConnectionService.current;boolean ready=ConnectionService.connected&&service!=null;submit.setEnabled(ready);status.setText(!ready?ConnectionService.status:service.accountLoginSupported()?"Đã kết nối PC · Nhập mật khẩu để đăng nhập.":"Cập nhật Lantern trên PC để đăng nhập bằng mật khẩu tài khoản.");}
    private void login(){
        if(busy||!active)return;ConnectionService service=ConnectionService.current;if(service==null||!ConnectionService.connected){refresh();return;}
        String value=password.getText().toString();if(value.isEmpty()){password.setError("Nhập mật khẩu tài khoản trên PC");return;}
        busy=true;submit.setEnabled(false);password.setEnabled(false);status.setText("Đang xác thực với PC…");
        service.loginAccount(value,error->{busy=false;password.setText("");password.setEnabled(true);if(!active)return;if(error==null&&AppSession.unlocked()){openChat();return;}status.setText(error==null?"Phiên đăng nhập đã đóng. Đại ca đăng nhập lại.":error);submit.setEnabled(ConnectionService.connected);password.requestFocus();});
    }
    private void openChat(){startActivity(new Intent(this,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP|Intent.FLAG_ACTIVITY_NEW_TASK));finish();}
}
