package vn.lantern.companion;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.database.Cursor;
import android.graphics.*;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.*;
import android.provider.OpenableColumns;
import android.text.InputType;
import android.view.*;
import android.view.inputmethod.InputMethodManager;
import android.widget.*;
import androidx.core.content.FileProvider;
import com.google.zxing.integration.android.*;
import org.json.*;
import java.io.File;
import java.time.OffsetDateTime;
import java.time.format.DateTimeFormatter;

public final class MainActivity extends Activity {
    private static final int BG=Color.rgb(16,26,33),SURFACE=Color.rgb(28,43,53),TEXT=Color.rgb(234,243,246),MUTED=Color.rgb(153,174,186),ACCENT=Color.rgb(45,185,167);
    private LinearLayout root,body,messagesBox,composerBar;private TextView subtitle,title,quote,actionStatus;private EditText composer;private ScrollView scroll;private Button sendButton;private boolean sending;
    private String selectedName="",replyTo="",renderKey="";private boolean resumed,locked;
    private final Handler tick=new Handler(Looper.getMainLooper());
    private TextView updateBanner;
    private final Runnable updateChanged=this::renderUpdate;
    private final Runnable expiryTick=new Runnable(){public void run(){if(resumed){refresh();tick.postDelayed(this,1000);}}};
    private int dp(float n){return (int)(getResources().getDisplayMetrics().density*n+.5f);}
    private GradientDrawable shape(int color,int radius){GradientDrawable d=new GradientDrawable();d.setColor(color);d.setCornerRadius(dp(radius));return d;}
    private TextView text(String value,int size,int color){TextView view=new TextView(this);view.setText(value);view.setTextSize(size);view.setTextColor(color);return view;}
    private String delivery(String state){return switch(state){case "sending"->"Đang gửi tới PC…";case "retry"->"Chưa xác nhận · Chạm để thử lại";case "queued"->"PC đã nhận · Đang chuyển tiếp";case "delivered"->"Đã nhận";case "read"->"Đã đọc";case "offered"->"Đã gửi";case "receiving"->"Đang nhận";case "received"->"Đã nhận";case "declined"->"Đã từ chối";case "error"->"Lỗi gửi";default->"Đã gửi";};}
    private Button button(String label,Runnable click){Button b=new Button(this);b.setText(label);b.setTextColor(TEXT);b.setTextSize(14);b.setAllCaps(false);b.setBackground(shape(SURFACE,14));b.setPadding(dp(14),dp(8),dp(14),dp(8));b.setOnClickListener(v->click.run());return b;}
    private LinearLayout column(){LinearLayout panel=new LinearLayout(this);panel.setOrientation(LinearLayout.VERTICAL);return panel;}
    public void onCreate(Bundle state){super.onCreate(state);if(Build.VERSION.SDK_INT>=30)getWindow().setDecorFitsSystemWindows(false);getWindow().setFlags(WindowManager.LayoutParams.FLAG_SECURE,WindowManager.LayoutParams.FLAG_SECURE);build();}
    protected void onResume(){super.onResume();resumed=true;ConnectionService.visible=true;ConnectionService.changed=this::refresh;try{if(DeviceKeys.load(this)!=null&&ConnectionService.current==null)startConnection();else if(ConnectionService.current!=null&&!ConnectionService.selected.isEmpty())ConnectionService.current.select(ConnectionService.selected);}catch(Exception e){toast("Không đọc được ghép đôi. Hãy quét QR lại.");}refresh();tick.postDelayed(expiryTick,1000);MobileUpdater.get(this).listen(updateChanged);MobileUpdater.get(this).automatic();}
    protected void onPause(){resumed=false;ConnectionService.visible=false;ConnectionService.displayed=new ConnectionService.ReadState("",java.util.Collections.emptySet());ConnectionService.changed=null;tick.removeCallbacks(expiryTick);MobileUpdater.get(this).unlisten(updateChanged);super.onPause();}
    private void build(){
        renderKey="";sending=false;ConnectionService.displayed=new ConnectionService.ReadState("",java.util.Collections.emptySet());root=column();root.setBackgroundColor(BG);root.setPadding(dp(18),dp(18),dp(18),dp(12));setContentView(root);
        root.setOnApplyWindowInsetsListener((v,insets)->{if(Build.VERSION.SDK_INT>=30){android.graphics.Insets bars=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.ime());root.setPadding(dp(18),bars.top+dp(12),dp(18),bars.bottom+dp(12));}return insets;});
        LinearLayout header=new LinearLayout(this);header.setGravity(Gravity.CENTER_VERTICAL);root.addView(header);
        if(!ConnectionService.selected.isEmpty())header.addView(button("‹",()->openList()),new LinearLayout.LayoutParams(dp(44),dp(44)));
        LinearLayout headings=column();headings.setPadding(dp(8),0,0,0);title=text(ConnectionService.selected.isEmpty()?"Lantern":selectedName,25,TEXT);title.setTypeface(null,Typeface.BOLD);headings.addView(title);subtitle=text(ConnectionService.status,12,MUTED);headings.addView(subtitle);header.addView(headings,new LinearLayout.LayoutParams(0,-2,1));
        header.addView(button("⋯",this::menu),new LinearLayout.LayoutParams(dp(44),dp(44)));
        updateBanner=text("",13,ACCENT);updateBanner.setPadding(dp(8),dp(10),dp(8),0);updateBanner.setOnClickListener(v->openUpdates());root.addView(updateBanner);renderUpdate();
        body=column();LinearLayout.LayoutParams b=new LinearLayout.LayoutParams(-1,0,1);b.topMargin=dp(20);root.addView(body,b);
        if(!ConnectionService.selected.isEmpty()){
            scroll=new ScrollView(this);scroll.setFillViewport(true);messagesBox=column();messagesBox.setPadding(0,dp(8),0,dp(8));scroll.addView(messagesBox);body.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));
            scroll.setOnScrollChangeListener((v,x,y,oldX,oldY)->updateVisibleMessages());
            quote=text("",12,ACCENT);quote.setVisibility(View.GONE);quote.setPadding(dp(12),dp(8),dp(12),dp(8));quote.setOnClickListener(v->{replyTo="";quote.setVisibility(View.GONE);});body.addView(quote);
            actionStatus=text("",12,MUTED);actionStatus.setPadding(dp(4),dp(6),dp(4),dp(6));actionStatus.setVisibility(View.GONE);body.addView(actionStatus);
            composerBar=new LinearLayout(this);composerBar.setBaselineAligned(false);composerBar.setGravity(Gravity.BOTTOM);body.addView(composerBar);
            composerBar.addView(button("＋",this::pickFile),new LinearLayout.LayoutParams(dp(44),dp(48)));
            composer=new EditText(this);composer.setContentDescription("Soạn tin nhắn");composer.setTextColor(TEXT);composer.setHintTextColor(MUTED);composer.setTextSize(16);composer.setHint("Nhắn tin…");composer.setInputType(InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_FLAG_MULTI_LINE|InputType.TYPE_TEXT_FLAG_CAP_SENTENCES);composer.setMaxLines(5);composer.setBackground(shape(SURFACE,18));composer.setPadding(dp(14),dp(10),dp(14),dp(10));LinearLayout.LayoutParams cp=new LinearLayout.LayoutParams(0,-2,1);cp.setMargins(dp(8),0,dp(8),0);composerBar.addView(composer,cp);
            sendButton=button("↑",this::send);sendButton.setContentDescription("Gửi tin nhắn");sendButton.setPadding(0,0,0,0);sendButton.setTextSize(24);sendButton.setBackground(shape(Color.rgb(18,116,107),16));composerBar.addView(sendButton,new LinearLayout.LayoutParams(dp(48),dp(48)));
        }
        refresh();
    }
    private void refresh(){
        if(!resumed||subtitle==null)return;subtitle.setText(ConnectionService.status);
        if(!ConnectionService.selected.isEmpty()&&sendButton!=null){sendButton.setEnabled(!sending&&ConnectionService.connected);sendButton.setAlpha(sendButton.isEnabled()?1f:.45f);if(!sending&&!ConnectionService.connected)showAction(ConnectionService.problem.isEmpty()?"Đang kết nối PC · Có thể xem lịch sử đã lưu và soạn tin.":ConnectionService.problem+" · Nội dung đang soạn vẫn được giữ.");}
        if(ConnectionService.selected.isEmpty())renderContacts();else renderMessages();
    }
    private void renderContacts(){
        String signature=ConnectionService.conversations.toString()+ConnectionService.status;if(signature.equals(renderKey))return;renderKey=signature;body.removeAllViews();
        if(ConnectionService.conversations.length()==0){
            if(ConnectionService.current!=null){LinearLayout connection=new LinearLayout(this);connection.setGravity(Gravity.CENTER_VERTICAL);ProgressBar progress=new ProgressBar(this);connection.addView(progress,new LinearLayout.LayoutParams(dp(24),dp(24)));TextView state=text(ConnectionService.problem.isEmpty()?ConnectionService.status:ConnectionService.problem,14,ACCENT);state.setPadding(dp(12),0,0,0);connection.addView(state,new LinearLayout.LayoutParams(0,-2,1));body.addView(connection);}
            TextView intro=text("Giữ cuộc trò chuyện\nluôn ở bên bạn.",32,TEXT);intro.setTypeface(null,Typeface.BOLD);intro.setPadding(0,dp(36),0,dp(16));body.addView(intro);
            TextView hint=text("Mở Lantern trên PC → Cài đặt → Điện thoại. Quét mã QR và xác nhận thiết bị để dùng cùng tài khoản qua Wi-Fi hoặc Internet.",16,MUTED);hint.setLineSpacing(dp(5),1);body.addView(hint);
            LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,dp(54));p.topMargin=dp(24);Button scan=button("Quét mã QR trên PC",this::scan);scan.setBackground(shape(Color.rgb(18,116,107),16));body.addView(scan,p);return;
        }
        TextView label=text("HỘI THOẠI",12,MUTED);label.setLetterSpacing(.12f);body.addView(label);
        ScrollView list=new ScrollView(this);LinearLayout rows=column();list.addView(rows);body.addView(list);
        for(int i=0;i<ConnectionService.conversations.length();i++)try{
            JSONObject conversation=ConnectionService.conversations.getJSONObject(i);String id=conversation.getString("id"),name=conversation.getString("name");
            LinearLayout row=new LinearLayout(this);row.setGravity(Gravity.CENTER_VERTICAL);row.setBackground(shape(SURFACE,18));row.setPadding(dp(14),dp(16),dp(14),dp(16));LinearLayout.LayoutParams rp=new LinearLayout.LayoutParams(-1,-2);rp.topMargin=dp(10);rows.addView(row,rp);
            TextView avatar=text(name.isEmpty()?"L":name.substring(0,name.offsetByCodePoints(0,1)).toUpperCase(java.util.Locale.getDefault()),20,TEXT);avatar.setGravity(Gravity.CENTER);avatar.setBackground(shape(Color.rgb(28,92,88),18));row.addView(avatar,new LinearLayout.LayoutParams(dp(48),dp(48)));
            LinearLayout names=column();names.setPadding(dp(14),0,0,0);TextView labelName=text(name,17,TEXT);labelName.setTypeface(null,Typeface.BOLD);names.addView(labelName);
            String presence=conversation.optString("presence");String state=switch(presence){case "online"->"● Online";case "idle"->"● Vắng mặt";case "dnd"->"● Không làm phiền";case "group"->"Nhóm";default->"● Offline";};
            names.addView(text((conversation.optBoolean("locked")?"🔒  ":"")+state,12,switch(presence){case "online"->ACCENT;case "idle"->Color.rgb(220,168,34);case "dnd"->Color.rgb(217,74,83);default->MUTED;}));row.addView(names,new LinearLayout.LayoutParams(0,-2,1));
            if(conversation.optInt("unread")>0){TextView unread=text(" "+conversation.optInt("unread")+" ",13,TEXT);unread.setBackground(shape(Color.rgb(18,116,107),12));unread.setPadding(dp(5),dp(3),dp(5),dp(3));row.addView(unread);}
            row.setOnClickListener(v->{ConnectionService service=ConnectionService.current;if(service!=null)service.select(id);else{ConnectionService.selected=id;ConnectionService.messages=new JSONArray();try{JSONObject config=DeviceKeys.load(this);if(config!=null&&!conversation.optBoolean("locked",true))ConnectionService.messages=new HistoryCache(this,config.getString("pc")).messages(id);}catch(Exception ignored){}}selectedName=name;replyTo="";build();});
        }catch(JSONException ignored){}
    }
    private void renderMessages(){
        boolean nextLocked=true;for(int i=0;i<ConnectionService.conversations.length();i++){JSONObject c=ConnectionService.conversations.optJSONObject(i);if(c!=null&&ConnectionService.selected.equals(c.optString("id")))nextLocked=c.optBoolean("locked");}
        locked=nextLocked;composerBar.setVisibility(locked?View.GONE:View.VISIBLE);if(locked){quote.setVisibility(View.GONE);ConnectionService.displayed=new ConnectionService.ReadState("",java.util.Collections.emptySet());}
        boolean expiring=false;for(int i=0;i<ConnectionService.messages.length();i++){JSONObject m=ConnectionService.messages.optJSONObject(i);if(m!=null&&!m.isNull("expiresAt"))expiring=true;}
        String signature=ConnectionService.messages.toString()+locked+ConnectionService.images.keySet().toString()+ConnectionService.status+(expiring?System.currentTimeMillis()/1000:0);if(signature.equals(renderKey))return;renderKey=signature;
        int previousY=scroll.getScrollY();boolean bottom=previousY+scroll.getHeight()>=messagesBox.getHeight()-dp(120);messagesBox.removeAllViews();
        if(locked){messagesBox.addView(text("🔒  Hội thoại được bảo vệ",21,TEXT));messagesBox.addView(text("Nhập mật khẩu để mở trong phiên kết nối này.",14,MUTED));messagesBox.addView(button("Mở khóa hội thoại",this::unlock));return;}
        java.util.List<JSONObject> visibleMessages=new java.util.ArrayList<>();
        for(int i=0;i<ConnectionService.messages.length();i++)try{
            JSONObject message=ConnectionService.messages.getJSONObject(i);
            if(!message.isNull("expiresAt")&&OffsetDateTime.parse(message.getString("expiresAt")).toInstant().toEpochMilli()<=System.currentTimeMillis()){File expired=ConnectionService.images.remove(message.optString("id"));if(expired!=null)expired.delete();continue;}
            visibleMessages.add(message);
        }catch(Exception ignored){}
        for(int i=0;i<visibleMessages.size();i++)try{
            JSONObject message=visibleMessages.get(i);
            boolean joinsPrevious=i>0&&MessageGrouping.joins(visibleMessages.get(i-1),message),joinsNext=i+1<visibleMessages.size()&&MessageGrouping.joins(message,visibleMessages.get(i+1));
            boolean own=message.optBoolean("outgoing");LinearLayout row=new LinearLayout(this);row.setTag(message.getString("id"));row.setGravity(own?Gravity.RIGHT:Gravity.LEFT);LinearLayout.LayoutParams rp=new LinearLayout.LayoutParams(-1,-2);rp.topMargin=dp(joinsPrevious?4:12);messagesBox.addView(row,rp);
            LinearLayout bubble=column();bubble.setPadding(dp(12),dp(10),dp(12),dp(8));bubble.setBackground(shape(own?Color.rgb(29,77,72):SURFACE,16));row.addView(bubble,new LinearLayout.LayoutParams(-2,-2));int max=getResources().getDisplayMetrics().widthPixels-dp(100);
            if(!own&&!joinsPrevious){TextView sender=text(message.optString("senderName"),12,ACCENT);sender.setMaxWidth(max);bubble.addView(sender);}
            String reply=message.optString("replyToId");if(!reply.isEmpty()){String snippet="Tin được trích dẫn";for(int j=0;j<ConnectionService.messages.length();j++){JSONObject q=ConnectionService.messages.optJSONObject(j);if(q!=null&&reply.equals(q.optString("id")))snippet=q.optString("text",q.optString("fileName"));}TextView q=text("↳ "+snippet,12,MUTED);q.setMaxWidth(max);q.setMaxLines(2);bubble.addView(q);}
            String kind=message.optString("kind");if("text".equals(kind)){TextView content=text(message.optString("text"),16,TEXT);content.setMaxWidth(max);content.setTextIsSelectable(true);bubble.addView(content);}
            else if("contact".equals(kind)){JSONObject contact=message.optJSONObject("contact");bubble.addView(text("👤 "+(contact==null?"Liên hệ":contact.optString("name")),16,TEXT));}
            else {
                File file=ConnectionService.images.get(message.optString("id"));if(file!=null){BitmapFactory.Options options=new BitmapFactory.Options();options.inJustDecodeBounds=true;BitmapFactory.decodeFile(file.getPath(),options);options.inSampleSize=Math.max(1,Math.max(options.outWidth,options.outHeight)/1000);options.inJustDecodeBounds=false;Bitmap bitmap=BitmapFactory.decodeFile(file.getPath(),options);if(bitmap!=null){ImageView image=new ImageView(this);image.setImageBitmap(bitmap);image.setAdjustViewBounds(true);image.setMaxWidth(max);image.setMaxHeight(dp(250));bubble.addView(image);}}
                TextView name=text(("video".equals(kind)?"▶ ":"image".equals(kind)?"▧ ":"📎 ")+message.optString("fileName"),14,TEXT);name.setMaxWidth(max);name.setMaxLines(3);bubble.addView(name);
                boolean ready=own||"received".equals(message.optString("status"));bubble.addView(button(ready?"Mở tệp":"Nhận tệp",()->attachment(message,ready)));
            }
            String stamp="";try{stamp=OffsetDateTime.parse(message.getString("time")).atZoneSameInstant(java.time.ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("HH:mm"));}catch(Exception ignored){}
            boolean attention=message.has("localRequest")||java.util.Arrays.asList("queued","sending","retry","error","receiving").contains(message.optString("status"));
            boolean timer=message.optInt("disappearAfterSeconds")>0;
            String metadata=joinsNext?attention?delivery(message.optString("status")):"":stamp+(own?" · "+delivery(message.optString("status")):"");
            TextView time=text(metadata+(timer?(metadata.isEmpty()?"◷":" · ◷"):""),10,MUTED);time.setPadding(0,dp(3),0,0);if(!joinsNext||attention||timer)bubble.addView(time);
            JSONObject reactions=message.optJSONObject("reactions");if(reactions!=null){StringBuilder emojis=new StringBuilder();java.util.Iterator<String> keys=reactions.keys();while(keys.hasNext()){String e=reactions.optString(keys.next());if(!e.isEmpty())emojis.append(e);}if(emojis.length()>0)bubble.addView(text(emojis.toString(),15,TEXT));}
            if(message.has("localRequest")){time.setTextColor("retry".equals(message.optString("status"))?Color.rgb(241,176,88):MUTED);time.setOnClickListener(v->{ConnectionService service=ConnectionService.current;if(service!=null)service.retrySend(message.optString("id"));});}
            else {bubble.setOnLongClickListener(v->{messageMenu(message);return true;});time.setOnClickListener(v->messageMenu(message));}
        }catch(Exception ignored){}
        // fullScroll performs focus navigation and can move the IME away from the composer.
        scroll.post(()->{scroll.scrollTo(0,bottom?messagesBox.getBottom():previousY);updateVisibleMessages();});
    }
    private void updateVisibleMessages(){
        java.util.Set<String> ids=new java.util.HashSet<>();
        if(resumed&&!locked&&messagesBox!=null&&!ConnectionService.selected.isEmpty())for(int i=0;i<messagesBox.getChildCount();i++){View row=messagesBox.getChildAt(i);Rect visible=new Rect();if(row.getTag() instanceof String&&row.getGlobalVisibleRect(visible)&&visible.height()>=Math.min(row.getHeight(),dp(24)))ids.add((String)row.getTag());}
        ConnectionService.displayed=new ConnectionService.ReadState(ConnectionService.selected,java.util.Collections.unmodifiableSet(ids));
    }
    private void showAction(String value){if(actionStatus!=null){actionStatus.setText(value);actionStatus.setVisibility(value.isEmpty()?View.GONE:View.VISIBLE);}}
    private void send(){String content=composer.getText().toString().trim();if(content.isEmpty())return;ConnectionService service=ConnectionService.current;if(service==null){showAction("Đang nối lại PC. Nội dung đang soạn vẫn được giữ.");return;}String error=service.sendText(ConnectionService.selected,content,replyTo);if(error!=null){showAction(error);return;}composer.getText().clear();composer.requestFocus();replyTo="";quote.setVisibility(View.GONE);showAction("");renderMessages();scroll.post(()->scroll.scrollTo(0,messagesBox.getBottom()));}
    private void unlock(){
        String conversation=ConnectionService.selected;LinearLayout form=column();form.setPadding(dp(20),dp(8),dp(20),dp(8));EditText password=new EditText(this);password.setContentDescription("Mật khẩu hội thoại");password.setInputType(InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_VARIATION_PASSWORD);password.setSingleLine();form.addView(password);
        TextView state=text("",13,MUTED);state.setPadding(0,dp(12),0,0);form.addView(state);ProgressBar progress=new ProgressBar(this,null,android.R.attr.progressBarStyleHorizontal);progress.setIndeterminate(true);progress.setVisibility(View.GONE);form.addView(progress);
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle("Mở khóa hội thoại").setView(form).setNegativeButton("Hủy",null).setPositiveButton("Mở khóa",null).create();
        dialog.setOnShowListener(d->{password.requestFocus();dialog.getWindow().setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_STATE_ALWAYS_VISIBLE);Button confirm=dialog.getButton(AlertDialog.BUTTON_POSITIVE);confirm.setOnClickListener(v->{if(!ConnectionService.connected){state.setText("Đang kết nối lại PC. Đại ca đợi kết nối xong rồi mở khóa.");return;}confirm.setEnabled(false);password.setEnabled(false);progress.setVisibility(View.VISIBLE);state.setText("Đang xác thực mật khẩu với PC…");try{submit(ConnectionService.packet("unlock",conversation).put("password",password.getText().toString()),error->{progress.setVisibility(View.GONE);if(error==null){password.setText("");dialog.dismiss();showAction("Đã mở khóa · Đang đồng bộ tin mới…");}else{state.setText(error);confirm.setEnabled(true);password.setEnabled(true);password.requestFocus();}});}catch(Exception e){confirm.setEnabled(true);password.setEnabled(true);progress.setVisibility(View.GONE);state.setText("Không gửi được yêu cầu mở khóa.");}});});dialog.show();
    }
    private void messageMenu(JSONObject message){String[] choices={"↳ Trả lời","👍","❤️","😂","😮","😢","😡","🎉","🙏","Gỡ biểu cảm"};new AlertDialog.Builder(this).setItems(choices,(d,i)->{if(i==0){replyTo=message.optString("id");quote.setText("↳ "+message.optString("text",message.optString("fileName"))+"   ×");quote.setMaxLines(2);quote.setVisibility(View.VISIBLE);composer.requestFocus();}else try{submit(ConnectionService.packet("react",ConnectionService.selected).put("messageId",message.getString("id")).put("emoji",i==9?"":choices[i]),this::result);}catch(Exception ignored){}}).show();}
    private void attachment(JSONObject message,boolean ready){if(!ready){try{submit(ConnectionService.packet("accept",ConnectionService.selected).put("messageId",message.getString("id")),this::result);}catch(Exception ignored){}return;}ConnectionService service=ConnectionService.current;if(service==null)return;toast("Đang tải tệp từ PC…");service.download(message,file->{try{Uri uri=FileProvider.getUriForFile(this,getPackageName()+".files",file);String mime=getContentResolver().getType(uri);startActivity(new Intent(Intent.ACTION_VIEW).setDataAndType(uri,mime==null?"application/octet-stream":mime).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION));}catch(Exception e){toast("Điện thoại chưa có ứng dụng mở loại tệp này.");}},this::toast);}
    private void pickFile(){startActivityForResult(new Intent(Intent.ACTION_OPEN_DOCUMENT).setType("*/*").addCategory(Intent.CATEGORY_OPENABLE),40);}
    private void submit(JSONObject request,java.util.function.Consumer<String> done){ConnectionService service=ConnectionService.current;if(service==null)done.accept("Chưa kết nối PC");else service.submit(request,done);}
    private void result(String error){if(error!=null)toast(error);}
    private void openUpdates(){startActivity(new Intent(this,UpdateActivity.class));}
    private void renderUpdate(){if(updateBanner==null)return;MobileUpdater u=MobileUpdater.get(this);updateBanner.setVisibility(u.release==null?View.GONE:View.VISIBLE);updateBanner.setText(u.ready?"↑ Bản "+u.release.version+" đã tải · Chạm để cập nhật":u.release==null?"":"↑ Có bản "+u.release.version+" · Chạm để xem");}
    private void menu(){String[] items=ConnectionService.selected.isEmpty()?new String[]{"Ghép đôi PC bằng QR","Ngắt kết nối","Quên PC này","Cập nhật ứng dụng"}:new String[]{"Khóa hội thoại","Danh sách hội thoại","Cập nhật ứng dụng"};new AlertDialog.Builder(this).setItems(items,(d,i)->{if(i==items.length-1){openUpdates();return;}if(!ConnectionService.selected.isEmpty()){if(i==1)openList();else try{submit(ConnectionService.packet("lock",ConnectionService.selected),this::result);ConnectionService.messages=new JSONArray();ConnectionService.clearMedia();}catch(Exception ignored){}return;}if(i==0)scan();else if(i==1)stopService(new Intent(this,ConnectionService.class));else new AlertDialog.Builder(this).setMessage("Xóa ghép đôi trên điện thoại? Đại ca có thể quét QR lại từ PC.").setNegativeButton("Hủy",null).setPositiveButton("Quên PC",(a,b)->{stopService(new Intent(this,ConnectionService.class));DeviceKeys.forget(this);openList();}).show();}).show();}
    private void openList(){ConnectionService.selected="";ConnectionService.messages=new JSONArray();ConnectionService.clearMedia();replyTo="";build();}
    private void scan(){new IntentIntegrator(this).setDesiredBarcodeFormats(IntentIntegrator.QR_CODE).setPrompt("Quét QR trong Lantern trên PC").setBeepEnabled(false).setOrientationLocked(false).initiateScan();}
    private void startConnection(){if(Build.VERSION.SDK_INT>=33&&checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED)requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS},7);startForegroundService(new Intent(this,ConnectionService.class));}
    protected void onActivityResult(int request,int result,Intent data){super.onActivityResult(request,result,data);IntentResult scan=IntentIntegrator.parseActivityResult(request,result,data);if(scan!=null){if(scan.getContents()!=null)try{
        JSONObject config=PcConnection.validateQr(scan.getContents());String code=DeviceKeys.code(config.getString("pc"));new AlertDialog.Builder(this).setTitle("Đối chiếu với PC").setMessage(config.optString("name")+"\n\n"+code+"\n\nBấm Kết nối, sau đó chấp nhận trên PC khi hai mã giống nhau.").setNegativeButton("Hủy",null).setPositiveButton("Kết nối",(d,w)->{try{if(ConnectionService.current!=null){toast("Hãy ngắt kết nối hiện tại rồi quét lại.");return;}DeviceKeys.save(this,config);startConnection();}catch(Exception e){toast("Không lưu được ghép đôi");}}).show();
        }catch(Exception e){toast(e.getMessage()==null?"Không đọc được QR":e.getMessage());}return;}
        if(request==40&&result==RESULT_OK&&data!=null&&data.getData()!=null){Uri uri=data.getData();String name="tep";try(Cursor cursor=getContentResolver().query(uri,new String[]{OpenableColumns.DISPLAY_NAME},null,null,null)){if(cursor!=null&&cursor.moveToFirst())name=cursor.getString(0);}ConnectionService service=ConnectionService.current;if(service!=null){toast("Đang gửi tệp…");service.upload(uri,name,e->{if(e==null)toast("Đã chuyển tệp tới PC");else toast(e);});}}
    }
    public void onBackPressed(){if(!ConnectionService.selected.isEmpty())openList();else super.onBackPressed();}
    private void toast(String message){if(resumed&&message!=null)Toast.makeText(this,message,Toast.LENGTH_LONG).show();}
}
