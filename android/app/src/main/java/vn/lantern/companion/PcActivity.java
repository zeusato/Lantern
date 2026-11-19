package vn.lantern.companion;

import android.app.*;
import android.content.*;
import android.graphics.Color;
import android.net.Uri;
import android.os.*;
import android.view.*;
import android.widget.*;
import android.util.Base64;
import androidx.core.view.ViewCompat;
import androidx.core.view.WindowCompat;
import androidx.core.view.WindowInsetsCompat;
import org.json.*;
import java.io.*;
import java.security.MessageDigest;
import java.util.*;
import java.util.concurrent.*;

/** One-shot PC operations. No chat outbox, offline queue or automatic command retries. */
public final class PcActivity extends Activity {
    private final ExecutorService worker=Executors.newSingleThreadExecutor();
    private final Handler ui=new Handler(Looper.getMainLooper());
    private LinearLayout body; private TextView status; private ProgressBar progress;
    private ConnectionService service; private PcConnection connection;
    private volatile boolean active,cancelled; private boolean busy;
    private String root="",path="",rootName=""; private int page;
    private File savedFile; private String savedName;
    private interface Job { void run() throws Exception; }
    private interface Reply { void accept(JSONObject value) throws Exception; }
    private int dp(int n){return Math.round(n*getResources().getDisplayMetrics().density);}
    @Override public void onCreate(Bundle state){
        super.onCreate(state);if(!AppSession.guard(this))return;getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);WindowCompat.setDecorFitsSystemWindows(getWindow(),false);
        LinearLayout layout=new LinearLayout(this);layout.setOrientation(LinearLayout.VERTICAL);layout.setPadding(dp(18),dp(16),dp(18),dp(16));layout.setBackgroundColor(Color.rgb(16,24,29));
        TextView heading=text("Máy tính",24);layout.addView(heading);
        progress=new ProgressBar(this,null,android.R.attr.progressBarStyleHorizontal);progress.setIndeterminate(true);progress.setContentDescription("Đang xử lý yêu cầu PC");progress.setVisibility(View.GONE);layout.addView(progress,new LinearLayout.LayoutParams(-1,dp(4)));
        ScrollView scroll=new ScrollView(this);body=new LinearLayout(this);body.setOrientation(LinearLayout.VERTICAL);scroll.addView(body);layout.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));
        status=text("",14);status.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);layout.addView(status);setContentView(layout);
        ViewCompat.setOnApplyWindowInsetsListener(layout,(v,insets)->{androidx.core.graphics.Insets bars=insets.getInsets(WindowInsetsCompat.Type.systemBars()|WindowInsetsCompat.Type.displayCutout());v.setPadding(bars.left+dp(18),bars.top+dp(16),bars.right+dp(18),Math.max(bars.bottom,insets.getInsets(WindowInsetsCompat.Type.ime()).bottom)+dp(16));return insets;});ViewCompat.requestApplyInsets(layout);
        service=ConnectionService.current;connection=service==null?null:service.controlConnection();
        if(state!=null&&state.containsKey("download")){File file=new File(state.getString("download",""));try{if(file.getCanonicalPath().startsWith(new File(getCacheDir(),"attachments").getCanonicalPath()+File.separator)&&file.isFile()){savedFile=file;savedName=state.getString("name","tep");}}catch(IOException ignored){}}
    }
    @Override protected void onResume(){super.onResume();if(!AppSession.guard(this))return;active=true;if(cancelled)note("Thao tác đã dừng khi rời màn hình. Bấm kiểm tra lại để lấy trạng thái mới.");if(body.getChildCount()==0&&!busy)home();}
    @Override protected void onPause(){active=false;cancelled=true;super.onPause();}
    @Override protected void onDestroy(){active=false;cancelled=true;worker.shutdownNow();super.onDestroy();}
    @Override protected void onSaveInstanceState(Bundle state){super.onSaveInstanceState(state);if(savedFile!=null){state.putString("download",savedFile.getAbsolutePath());state.putString("name",savedName);}}
    private TextView text(String value,int size){TextView view=new TextView(this);view.setText(value);view.setTextColor(Color.rgb(226,237,239));view.setTextSize(size);view.setPadding(0,dp(8),0,dp(8));return view;}
    private Button button(String title,Runnable action){Button button=new Button(this);button.setAllCaps(false);button.setText(title);button.setOnClickListener(v->{if(!busy&&active)action.run();});body.addView(button,new LinearLayout.LayoutParams(-1,-2));return button;}
    private void note(String value){if(active)status.setText(value);}
    private void check()throws IOException{if(!active||cancelled||Thread.currentThread().isInterrupted())throw new IOException("Đã dừng thao tác. Mở lại mục Máy tính để kiểm tra.");}
    private JSONObject call(JSONObject command)throws Exception{check();if(service==null||connection==null)throw new IOException("Kết nối và cập nhật Lantern PC để dùng chức năng này.");return service.controlRequest(connection,command);}
    private JSONObject command(String action)throws JSONException{return new JSONObject().put("action",action);}
    private void run(Job job){
        if(busy||!active)return;busy=true;cancelled=false;
        progress.setVisibility(View.VISIBLE);List<View> disabled=new ArrayList<>();
        for(int i=0;i<body.getChildCount();i++){View view=body.getChildAt(i);if(view instanceof Button&&view.isEnabled()&&!"cancel-download".equals(view.getTag())){disabled.add(view);view.setEnabled(false);}}
        worker.execute(()->{try{job.run();}catch(Exception e){ui.post(()->note(e instanceof TimeoutException||e instanceof java.net.SocketTimeoutException?"PC chưa trả kết quả. Kiểm tra kết nối rồi thử kiểm tra lại.":e.getMessage()==null?"Kết nối PC bị gián đoạn. Mở lại mục Máy tính.":e.getMessage()));}finally{ui.post(()->{busy=false;progress.setVisibility(View.GONE);for(View view:disabled)if(view.getParent()==body)view.setEnabled(true);if(active&&cancelled)note("Thao tác đã dừng khi rời màn hình. Bấm kiểm tra lại để lấy trạng thái mới.");});}});
    }
    private void request(JSONObject command,Reply done){run(()->{JSONObject result=call(command);ui.post(()->{if(active&&!cancelled)try{done.accept(result);}catch(Exception e){note(e.getMessage());}});});}
    private void home(){try{
        note("Đang đọc quyền PC…");request(command("status"),access->{body.removeAllViews();
            body.addView(text("PC cần đang chạy và mở khóa Lantern. Cấp quyền tại Cài đặt → Điện thoại trên PC.",14));
            button("Khóa Windows",()->prepare("power.lock",null,"Khóa Windows?")).setEnabled(access.optBoolean("power"));
            button("Tắt máy",()->prepare("power.shutdown",null,"Tắt PC? Tài liệu chưa lưu có thể chặn quá trình này.")).setEnabled(access.optBoolean("power"));
            button("AI · Hiện cửa sổ và kiểm tra",this::inspect).setEnabled(access.optBoolean("ai"));
            body.addView(text("Tệp trên PC",19));JSONArray roots=access.optJSONArray("roots");
            if(roots==null||roots.length()==0)body.addView(text("Chưa được cấp thư mục nào.",14));
            else for(int i=0;i<roots.length();i++){JSONObject r=roots.getJSONObject(i);button("📁 "+r.getString("name"),()->{root=r.optString("id");rootName=r.optString("name");path="";page=0;browse();});}
            if(savedFile!=null&&savedFile.isFile())button("Lưu tệp đã tải: "+savedName,this::save);
            note("Đã kết nối. Lệnh không được xếp hàng khi mất mạng.");
        });}catch(Exception e){note(e.getMessage());}}
    private void prepare(String action,JSONObject arguments,String question){try{
        JSONObject request=arguments==null?command("prepare"):new JSONObject(arguments.toString()).put("action","prepare");request.put("command",action);
        request(request,ticket->{String token=ticket.getString("ticket");
            new AlertDialog.Builder(this).setTitle("Xác nhận lệnh PC").setMessage(question+"\n\nLệnh hết hạn sau 45 giây và chỉ thực hiện một lần.").setNegativeButton("Hủy",null).setPositiveButton("Thực hiện",(d,w)->execute(token)).show();
        });}catch(Exception e){note(e.getMessage());}}
    private void execute(String ticket){try{
        body.removeAllViews();note("Đang gửi lệnh…");
        button("Kiểm tra kết quả",()->{try{request(command("result").put("ticket",ticket),r->note(r.optString("message",r.optString("state"))));}catch(Exception e){note(e.getMessage());}});
        button("Xem câu trả lời AI",this::inspect);button("Về mục PC",this::home);
        run(()->{try{JSONObject result=call(command("execute").put("ticket",ticket));ui.post(()->note(result.optString("message",result.optString("state"))));}
            catch(Exception e){ui.post(()->note("Chưa xác định kết quả. Không tự gửi lại; bấm Kiểm tra kết quả."));}});
    }catch(Exception e){note(e.getMessage());}}
    private void inspect(){try{note("Đang hiện cửa sổ và đọc trạng thái AI…");request(command("ai.inspect"),result->{body.removeAllViews();button("‹ Về mục PC",this::home);JSONArray targets=result.optJSONArray("targets");
        if(targets==null||targets.length()==0)body.addView(text("Không tìm thấy Claude hoặc ChatGPT/Codex đang chạy.",15));
        else for(int i=0;i<targets.length();i++){JSONObject target=targets.getJSONObject(i);String state=target.optString("state");String label=switch(state){case "idle"->"Sẵn sàng";case "busy"->"Đang làm việc";case "attention"->"Cần phản hồi trên PC";case "draft"->"Có bản nháp";default->"Chưa xác định";};
            body.addView(text(target.optString("app")+" · "+label,18));body.addView(text(target.optString("conversation")+"\n"+target.optString("detail"),14));
            TextView answer=text(target.optString("answer", "Chưa đọc được câu trả lời trong giao diện hiện tại."),15);answer.setTextIsSelectable(true);body.addView(answer);
            if(!target.optString("version").isEmpty())button("Theo dõi câu trả lời",()->watch(target));
            if(state.equals("idle"))button("Gửi yêu cầu",()->prompt(target));
        }button("Kiểm tra lại",this::inspect);note("Không tự vượt xác nhận của AI hoặc ghi đè bản nháp.");});}catch(Exception e){note(e.getMessage());}}
    private void prompt(JSONObject target){body.removeAllViews();body.addView(text(target.optString("app")+" · "+target.optString("conversation"),18));EditText input=new EditText(this);input.setMinLines(4);input.setTextColor(Color.WHITE);input.setHintTextColor(Color.LTGRAY);input.setHint("Yêu cầu tiếp theo…");input.setContentDescription("Yêu cầu cho AI");input.setFilters(new android.text.InputFilter[]{new android.text.InputFilter.LengthFilter(16000)});body.addView(input);
        button("Gửi yêu cầu",()->{String prompt=input.getText().toString().trim();if(prompt.isEmpty()){note("Nhập yêu cầu trước khi gửi.");return;}try{prepare("ai.send",new JSONObject().put("target",target.getString("id")).put("version",target.getString("version")).put("prompt",prompt),"Gửi vào "+target.optString("conversation")+":\n\n"+prompt);}catch(Exception e){note(e.getMessage());}});button("Kiểm tra lại",this::inspect);}
    private void watch(JSONObject target){
        body.removeAllViews();button("‹ Kiểm tra AI",this::inspect);
        TextView answer=text(target.optString("answer","Đang chờ câu trả lời…"),16);answer.setTextIsSelectable(true);body.addView(answer);
        Runnable poll=new Runnable(){public void run(){
            if(!active||answer.getParent()!=body)return;
            if(busy){ui.postDelayed(this,500);return;}
            try{request(command("ai.read").put("target",target.getString("id")).put("version",target.getString("version")),result->{
                if(answer.getParent()!=body)return;
                JSONObject next=result.getJSONArray("targets").getJSONObject(0);
                answer.setText(next.optString("answer", "Chưa có câu trả lời đọc được."));
                note("Câu trả lời gần nhất · Tự cập nhật mỗi 5 giây");ui.postDelayed(this,5000);
            });}catch(Exception e){note(e.getMessage());}
        }};poll.run();
    }
    private void browse(){try{note("Đang mở thư mục…");request(command("files.list").put("root",root).put("path",path).put("page",page),listing->{body.removeAllViews();body.addView(text(rootName+(path.isEmpty()?"":" / "+path),18));
        button("‹ "+(path.isEmpty()?"Về mục PC":"Thư mục cha"),()->{if(path.isEmpty())home();else{int index=path.lastIndexOf('/');path=index<0?"":path.substring(0,index);page=0;browse();}});
        JSONArray entries=listing.getJSONArray("entries");for(int i=0;i<entries.length();i++){JSONObject entry=entries.getJSONObject(i);String name=entry.getString("name");button((entry.getBoolean("directory")?"📁 ":"📄 ")+name+(entry.getBoolean("directory")?"":" · "+entry.optLong("size")/1024+" KB"),()->{if(name.contains("/")||name.contains("\\")||name.equals("..")||name.equals(".")){note("Tên tệp không hợp lệ.");return;}String next=path.isEmpty()?name:path+"/"+name;if(entry.optBoolean("directory")){path=next;page=0;browse();}else download(next);});}
        if(page>0)button("Trang trước",()->{page--;browse();});if(listing.optBoolean("more"))button("Trang tiếp",()->{page++;browse();});note(entries.length()==0?"Thư mục trống hoặc không có mục được phép đọc.":"Chọn tệp để tải. Tối đa 100 MB mỗi tệp.");});}catch(Exception e){note(e.getMessage());}}
    private void download(String filePath){
        body.removeAllViews();body.addView(text(filePath,18));Button cancel=button("Hủy tải",()->{});cancel.setTag("cancel-download");cancel.setOnClickListener(v->{cancelled=true;note("Đang hủy tải…");});button("Về thư mục",this::browse);note("Đang chuẩn bị tệp…");
        run(()->{
            JSONObject meta=call(command("files.open").put("root",root).put("path",filePath));String ticket=meta.getString("ticket");File temp=null;
            try{
                long size=meta.getLong("size");if(size<0||size>100L*1024*1024)throw new IOException("Tệp vượt 100 MB.");
                File folder=new File(getCacheDir(),"attachments");if(!folder.exists()&&!folder.mkdirs())throw new IOException("Không tạo được thư mục tải tạm.");
                temp=File.createTempFile("pc-",".tmp",folder);MessageDigest hash=MessageDigest.getInstance("SHA-256");
                try(FileOutputStream out=new FileOutputStream(temp)){long offset=0;while(offset<size){check();JSONObject r=call(command("files.read").put("ticket",ticket).put("offset",offset));byte[] bytes=Base64.decode(r.getString("data"),Base64.NO_WRAP);if(bytes.length==0||bytes.length>262144||r.getLong("offset")!=offset||r.getLong("size")!=size||offset+bytes.length>size)throw new IOException("Tệp tải không hợp lệ.");out.write(bytes);hash.update(bytes);offset+=bytes.length;long progress=offset*100/Math.max(1,size);ui.post(()->note("Đã tải "+progress+"%"));}}
                if(!DeviceKeys.hex(hash.digest()).equalsIgnoreCase(meta.getString("hash")))throw new IOException("Checksum không khớp; chưa lưu tệp.");check();
                File complete=temp;temp=null;String name=meta.getString("name");ui.post(()->{if(!active||cancelled){complete.delete();return;}if(savedFile!=null)savedFile.delete();savedFile=complete;savedName=name;body.removeAllViews();body.addView(text(name,18));button("Lưu tệp vào điện thoại",this::save);button("Về thư mục",this::browse);note("Đã tải và kiểm tra toàn vẹn tệp.");});
            }finally{if(temp!=null)temp.delete();try{service.controlRequest(connection,command("files.close").put("ticket",ticket));}catch(Exception ignored){}ui.post(()->{if(active&&cancelled){body.removeAllViews();button("Về thư mục",this::browse);}});}
        });
    }
    private void save(){if(savedFile!=null)startActivityForResult(new Intent(Intent.ACTION_CREATE_DOCUMENT).setType("application/octet-stream").addCategory(Intent.CATEGORY_OPENABLE).putExtra(Intent.EXTRA_TITLE,savedName),71);}
    @Override protected void onActivityResult(int request,int result,Intent data){super.onActivityResult(request,result,data);if(request!=71||result!=RESULT_OK||data==null||data.getData()==null||savedFile==null)return;File file=savedFile;Uri uri=data.getData();worker.execute(()->{try(InputStream in=new FileInputStream(file);OutputStream out=getContentResolver().openOutputStream(uri)){if(out==null)throw new IOException("Không mở được nơi lưu.");byte[] buffer=new byte[65536];int count;while((count=in.read(buffer))!=-1)out.write(buffer,0,count);ui.post(()->note("Đã lưu tệp vào điện thoại."));}catch(Exception e){ui.post(()->note("Không lưu được tệp. Chọn lại nơi lưu."));}});}
}
