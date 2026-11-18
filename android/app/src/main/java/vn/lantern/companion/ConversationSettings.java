package vn.lantern.companion;

import android.app.*;
import android.graphics.Color;
import android.text.*;
import android.view.View;
import android.widget.*;
import org.json.*;
import java.util.*;
import java.util.function.Consumer;

/** Conversation actions share the PC's authority and never queue destructive work offline. */
final class ConversationSettings {
    private final Activity activity;
    private final String id;
    private final Consumer<Set<String>> deleted;
    private AlertDialog dialog;
    private boolean busy;
    private Runnable availability;
    private TextView heading;
    private Button timer;
    private static final int[] TIMES={0,10,30,60,300,3600,86400,604800};
    ConversationSettings(Activity activity,String id,Consumer<Set<String>> deleted){this.activity=activity;this.id=id;this.deleted=deleted;}
    static JSONObject contact(String id){for(int i=0;i<ConnectionService.conversations.length();i++){JSONObject c=ConnectionService.conversations.optJSONObject(i);if(c!=null&&id.equals(c.optString("id")))return c;}return null;}
    private boolean allowed(){JSONObject c=contact(id);return id.equals(ConnectionService.selected)&&c!=null&&!c.optBoolean("locked",true);}
    private int dp(int n){return (int)(activity.getResources().getDisplayMetrics().density*n+.5f);}
    private LinearLayout panel(){LinearLayout p=new LinearLayout(activity);p.setOrientation(LinearLayout.VERTICAL);p.setPadding(dp(20),dp(8),dp(20),dp(12));return p;}
    private TextView note(String value){TextView t=new TextView(activity);t.setText(value);t.setTextColor(Color.rgb(153,174,186));t.setTextSize(14);t.setPadding(0,dp(8),0,dp(8));return t;}
    private EditText input(String label,String value){EditText e=new EditText(activity);e.setContentDescription(label);e.setHint(label);e.setSingleLine();e.setText(value);e.setFilters(new InputFilter[]{new InputFilter.LengthFilter(80)});return e;}
    private View scroll(LinearLayout content){ScrollView view=new ScrollView(activity);view.addView(content);return view;}
    private Button action(LinearLayout panel,String title,Runnable action){Button b=new Button(activity);b.setText(title);b.setAllCaps(false);b.setOnClickListener(v->action.run());panel.addView(b,new LinearLayout.LayoutParams(-1,-2));return b;}
    static String duration(int seconds){return switch(seconds){case 0->"Tắt";case 10->"10 giây";case 30->"30 giây";case 60->"1 phút";case 300->"5 phút";case 3600->"1 giờ";case 86400->"1 ngày";case 604800->"7 ngày";default->seconds+" giây";};}
    private AlertDialog open(AlertDialog.Builder builder){dismiss();AlertDialog next=builder.create();next.getWindow().addFlags(android.view.WindowManager.LayoutParams.FLAG_SECURE);dialog=next;next.setOnDismissListener(d->{if(dialog==next){dialog=null;availability=null;heading=null;timer=null;}});return next;}
    void dismiss(){AlertDialog previous=dialog;dialog=null;availability=null;heading=null;timer=null;busy=false;if(previous!=null)previous.dismiss();}
    void refresh(){if(dialog==null)return;if(!allowed()){dismiss();return;}if(heading!=null)heading.setText(contact(id).optString("name"));if(timer!=null)timer.setText("Tin nhắn tự hủy · "+duration(contact(id).optInt("disappearSeconds")));if(availability!=null)availability.run();}
    void show(){
        if(!allowed())return;
        JSONObject c=contact(id);LinearLayout p=panel();TextView name=note(c.optString("name"));name.setTextSize(19);p.addView(name);
        if(!c.optBoolean("isGroup"))action(p,"Đặt tên riêng",this::rename);
        Button timerAction=action(p,"Tin nhắn tự hủy · "+duration(c.optInt("disappearSeconds")),this::timer);
        if(c.optBoolean("protected"))action(p,"Khóa hội thoại ngay",()->{try{ConnectionService s=ConnectionService.current;if(s==null)return;s.submit(ConnectionService.packet("lock",id),e->{if(e!=null)Toast.makeText(activity,e,Toast.LENGTH_LONG).show();});dismiss();}catch(JSONException ignored){}});
        action(p,"Chọn tin nhắn để xóa",this::selectMessages);
        action(p,"Xóa lịch sử hội thoại",()->delete(null));
        p.addView(note("Đổi tên riêng áp dụng cho PC và điện thoại của bạn. Xóa lịch sử không xóa bản ở máy đối tác."));
        AlertDialog next=open(new AlertDialog.Builder(activity).setTitle("Cài đặt hội thoại").setView(scroll(p)).setNegativeButton("Đóng",null));heading=name;timer=timerAction;next.show();
    }
    private void bindSave(AlertDialog target,TextView status,boolean needsSettings,Runnable action){
        target.setOnShowListener(d->{
            Button save=target.getButton(AlertDialog.BUTTON_POSITIVE);
            availability=()->{ConnectionService s=ConnectionService.current;boolean online=ConnectionService.connected&&s!=null;boolean supported=!needsSettings||online&&s.conversationSettingsSupported();save.setEnabled(!busy&&online&&supported);if(!online)status.setText("Kết nối PC để lưu thay đổi.");else if(!supported)status.setText("Cập nhật Lantern trên PC để dùng tùy chỉnh hội thoại.");};
            availability.run();save.setOnClickListener(v->{if(!busy&&allowed())action.run();});
        });target.show();
    }
    private void submit(AlertDialog target,TextView status,JSONObject request,Runnable done){
        if(busy||!allowed())return;ConnectionService service=ConnectionService.current;if(service==null||!ConnectionService.connected){status.setText("Kết nối PC để lưu thay đổi.");return;}
        busy=true;target.getButton(AlertDialog.BUTTON_POSITIVE).setEnabled(false);status.setText("Đang lưu trên PC…");
        service.submit(request,error->{if(dialog!=target)return;busy=false;if(!allowed()){dismiss();return;}if(error!=null){status.setText(error);if(availability!=null)availability.run();return;}dismiss();done.run();});
    }
    private void rename(){
        if(!allowed())return;JSONObject c=contact(id);LinearLayout p=panel();
        p.addView(note("Chỉ bạn thấy tên này trên PC và điện thoại. Để trống để dùng lại tên gốc: "+c.optString("originalName",c.optString("name"))));
        EditText name=input("Tên riêng",c.optString("alias"));p.addView(name);TextView status=note("");p.addView(status);
        AlertDialog next=open(new AlertDialog.Builder(activity).setTitle("Đặt tên riêng").setView(scroll(p)).setNegativeButton("Hủy",null).setPositiveButton("Lưu",null));
        bindSave(next,status,true,()->{try{submit(next,status,ConnectionService.packet("conversation-rename",id).put("name",name.getText().toString().trim()),this::show);}catch(JSONException ignored){}});name.requestFocus();
    }
    private void timer(){
        if(!allowed())return;LinearLayout p=panel();p.addView(note("Áp dụng cho tin mới ở cả hai chiều. Đếm ngược từ khi người nhận đọc; trong nhóm là lượt đọc đầu tiên. Tin và tệp do Lantern lưu sẽ được xóa khi hết giờ."));
        int current=contact(id).optInt("disappearSeconds");List<Integer> times=new ArrayList<>();for(int t:TIMES)times.add(t);if(!times.contains(current))times.add(current);
        RadioGroup choices=new RadioGroup(activity);for(int t:times){RadioButton b=new RadioButton(activity);b.setId(View.generateViewId());b.setTag(t);b.setText(duration(t));choices.addView(b);if(t==current)choices.check(b.getId());}p.addView(choices);TextView status=note("");p.addView(status);
        AlertDialog next=open(new AlertDialog.Builder(activity).setTitle("Tin nhắn tự hủy").setView(scroll(p)).setNegativeButton("Hủy",null).setPositiveButton("Lưu",null));
        bindSave(next,status,true,()->{try{int seconds=(Integer)choices.findViewById(choices.getCheckedRadioButtonId()).getTag();submit(next,status,ConnectionService.packet("conversation-timer",id).put("disappearSeconds",seconds),this::show);}catch(JSONException ignored){}});
    }
    void selectMessages(){
        if(!allowed())return;List<String> ids=new ArrayList<>(),labels=new ArrayList<>();
        for(int i=0;i<ConnectionService.messages.length();i++){JSONObject m=ConnectionService.messages.optJSONObject(i);if(m==null||m.has("localRequest"))continue;ids.add(m.optString("id"));String snippet=m.optString("text");if(snippet.isBlank())snippet=m.optString("fileName","Tin nhắn");String label=(m.optBoolean("outgoing")?"Bạn":m.optString("senderName","Đối tác"))+": "+snippet.replace('\n',' ');labels.add(label.length()>100?label.substring(0,100)+"…":label);}
        if(ids.isEmpty()){Toast.makeText(activity,"Chưa có tin nhắn để chọn.",Toast.LENGTH_SHORT).show();return;}
        Set<String> selected=new LinkedHashSet<>();
        AlertDialog next=open(new AlertDialog.Builder(activity).setTitle("Chọn tin nhắn để xóa").setMultiChoiceItems(labels.toArray(new String[0]),null,(d,i,checked)->{if(checked)selected.add(ids.get(i));else selected.remove(ids.get(i));Button b=((AlertDialog)d).getButton(AlertDialog.BUTTON_POSITIVE);b.setEnabled(!selected.isEmpty());b.setText("Xóa ("+selected.size()+")");}).setNegativeButton("Hủy",null).setPositiveButton("Xóa",null));
        next.setOnShowListener(d->{next.getButton(AlertDialog.BUTTON_POSITIVE).setEnabled(false);next.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v->delete(selected));});next.show();
    }
    void delete(Set<String> selection){
        if(!allowed()||selection!=null&&selection.isEmpty())return;Set<String> ids=selection==null?null:new LinkedHashSet<>(selection);LinearLayout p=panel();
        p.addView(note("Xóa khỏi lịch sử của bạn trên PC và điện thoại đã ghép đôi. Bản ở máy đối tác vẫn còn. Không thể hoàn tác."));
        EditText phrase=ids==null?input("Nhập XÓA để xác nhận",""):null;if(phrase!=null)p.addView(phrase);TextView status=note("");p.addView(status);
        AlertDialog next=open(new AlertDialog.Builder(activity).setTitle(ids==null?"Xóa lịch sử hội thoại?":"Xóa "+ids.size()+" tin nhắn?").setView(scroll(p)).setNegativeButton("Hủy",null).setPositiveButton("Xóa",null));
        bindSave(next,status,false,()->{if(phrase!=null&&!"XÓA".equals(phrase.getText().toString().trim())){status.setText("Nhập đúng XÓA để xác nhận.");return;}try{JSONObject request=ConnectionService.packet(ids==null?"clear-history":"delete-messages",id).put("messageIds",new JSONArray(ids==null?Collections.emptyList():ids));submit(next,status,request,()->{deleted.accept(ids);Toast.makeText(activity,"Đã xóa khỏi lịch sử của bạn.",Toast.LENGTH_SHORT).show();});}catch(JSONException ignored){}});
    }
}
