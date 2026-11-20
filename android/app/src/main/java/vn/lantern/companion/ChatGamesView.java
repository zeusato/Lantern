package vn.lantern.companion;

import android.app.*;
import android.graphics.*;
import android.graphics.drawable.GradientDrawable;
import android.view.*;
import android.widget.*;
import android.text.InputFilter;
import org.json.*;
import java.util.*;
import java.util.function.*;

/** Native game surface. Rules, signatures and private fleet proofs stay on the paired PC. */
final class ChatGamesView extends ScrollView {
    private static final int TEXT=0xffeaf3f6,MUTED=0xff99aeba,ACCENT=0xff2db9a7,CORAL=0xffff927c,SURFACE=0xff1c2b35;
    private final Activity activity;private final String conversation;private final Consumer<Boolean> layoutChanged;
    private JSONObject view,state;private String key="",hidden="",draft="",lastError="";private EditText wordInput;private TextView error;private boolean busy,vertical,own;private int selectedShip=1;private int[] fleet=randomFleet();
    private static final int[] LENGTHS={5,4,3,3,2};
    private AlertDialog dialog;
    private void showDialog(AlertDialog.Builder builder){dismissDialog();dialog=builder.create();dialog.getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);dialog.show();}
    void dismissDialog(){if(dialog!=null){dialog.dismiss();dialog=null;}}
    protected void onDetachedFromWindow(){dismissDialog();super.onDetachedFromWindow();}
    record Draft(String game,String word,String hidden,int[] fleet,int ship,boolean vertical,boolean own){}
    Draft saveDraft(){return new Draft(state==null?"":state.optString("id"),wordInput==null?draft:wordInput.getText().toString(),hidden,fleet.clone(),selectedShip,vertical,own);}
    void restoreDraft(Draft saved){JSONObject current=ConnectionService.games.get(conversation);if(saved==null||current==null||!saved.game().equals(current.optJSONObject("state").optString("id")))return;draft=saved.word();hidden=saved.hidden();fleet=saved.fleet().clone();selectedShip=saved.ship();vertical=saved.vertical();own=saved.own();}
    ChatGamesView(Activity activity,String conversation,Consumer<Boolean> layoutChanged){super(activity);this.activity=activity;this.conversation=conversation;this.layoutChanged=layoutChanged;setFillViewport(true);setBackgroundColor(0xff13232c);setVisibility(GONE);}
    static String title(String kind){return switch(kind){case "caro"->"Caro";case "ships"->"Bắn tàu";default->"Nối chữ";};}
    private int dp(float n){return (int)(getResources().getDisplayMetrics().density*n+.5f);}
    private LinearLayout column(){LinearLayout p=new LinearLayout(activity);p.setOrientation(LinearLayout.VERTICAL);return p;}
    private GradientDrawable shape(int color){GradientDrawable d=new GradientDrawable();d.setColor(color);d.setCornerRadius(dp(12));return d;}
    private TextView text(String value,int size,int color){TextView t=new TextView(activity);t.setText(value);t.setTextSize(size);t.setTextColor(color);t.setPadding(0,dp(6),0,dp(6));return t;}
    private Button button(String label,Runnable action,boolean enabled){Button b=new Button(activity);b.setText(label);b.setTextSize(12);b.setAllCaps(false);b.setTextColor(TEXT);b.setBackground(shape(SURFACE));b.setMinWidth(0);b.setMinimumWidth(0);b.setPadding(dp(8),dp(4),dp(8),dp(4));b.setEnabled(enabled);b.setAlpha(enabled?1f:.45f);b.setOnClickListener(v->action.run());LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,-2);p.topMargin=dp(6);b.setLayoutParams(p);return b;}
    private boolean finished(){return Set.of("finished","declined","cancelled").contains(state.optString("status"));}
    private boolean pending(){return busy||view.optBoolean("pending");}
    private String me(){return view.optString("me");}
    private String other(){return state.optString("host").equals(me())?state.optString("guest"):state.optString("host");}
    void update(JSONObject next,boolean locked){
        JSONObject contact=ConversationSettings.contact(conversation);
        if(locked||!AppSession.unlocked()||contact==null||contact.optBoolean("locked",true)||next==null){dismissDialog();view=null;state=null;key="";wordInput=null;error=null;draft="";setVisibility(GONE);removeAllViews();layoutChanged.accept(false);return;}
        JSONObject nextState=next.optJSONObject("state");if(nextState==null)return;
        boolean show=!Set.of("invited","declined","cancelled").contains(nextState.optString("status"))&&!hidden.equals(nextState.optString("id"));
        setVisibility(show?VISIBLE:GONE);layoutChanged.accept(show);
        String nextKey=next.toString();if(nextKey.equals(key))return;
        if(wordInput!=null)draft=wordInput.getText().toString();
        if(state!=null&&(!state.optString("id").equals(nextState.optString("id"))||state.optJSONArray("words").length()!=nextState.optJSONArray("words").length()))draft="";
        if(state!=null&&!state.optString("id").equals(nextState.optString("id"))){fleet=randomFleet();selectedShip=1;own=false;vertical=false;lastError="";}
        key=nextKey;view=next;state=nextState;if(show)render();else{wordInput=null;error=null;removeAllViews();}
    }
    void open(){hidden="";key="";update(ConnectionService.games.get(conversation),false);}
    void invite(){
        JSONObject contact=ConversationSettings.contact(conversation);if(contact==null||contact.optBoolean("locked",true)||contact.optBoolean("isGroup")){toast("Chơi trong chat riêng đã mở khóa.");return;}
        if(view!=null&&!finished()){if("invited".equals(state.optString("status")))toast("Lời mời đang ở trong log chat.");else open();return;}
        showDialog(new AlertDialog.Builder(activity).setTitle("Chơi cùng nhau").setItems(new String[]{"Caro · Nối 5 quân","Bắn tàu · Khóa đội tàu và đối kháng","Nối chữ · Nối hai tiếng"},(d,i)->inviteKind(new String[]{"caro","ships","words"}[i])));
    }
    private void inviteKind(String kind){try{ConnectionService service=ConnectionService.current;if(service==null){toast("Chưa kết nối PC");return;}hidden="";service.submit(ConnectionService.packet("game-invite",conversation).put("name",kind),e->{if(e!=null)toast(e);});}catch(JSONException e){toast(e.getMessage());}}
    void action(String type){action(type,-1,"",null);}
    private void action(String type,int cell,String word,int[] board){
        if(state==null||pending())return;JSONObject contact=ConversationSettings.contact(conversation);if(contact==null||contact.optBoolean("locked",true))return;
        try{
            JSONObject packet=ConnectionService.packet("game-action",conversation).put("messageId",state.getString("id")).put("name",type).put("offset",cell).put("text",word).put("gameRevision",state.getInt("revision"));
            if(board!=null){JSONArray array=new JSONArray();for(int value:board)array.put(value);packet.put("data",array.toString());}
            ConnectionService service=ConnectionService.current;if(service==null){toast("Chưa kết nối PC");return;}busy=true;
            lastError="";service.submit(packet,e->{busy=false;if(e!=null){lastError=e;if(error!=null)error.setText(e);else toast(e);if(wordInput!=null){EditText field=wordInput;field.animate().translationX(dp(4)).setDuration(65).withEndAction(()->field.animate().translationX(0).setDuration(65)).start();}}key="";update(ConnectionService.games.get(conversation),false);});
        }catch(JSONException e){busy=false;toast(e.getMessage());}
    }
    private void toast(String value){Toast.makeText(activity,value,Toast.LENGTH_LONG).show();}
    private void render(){
        wordInput=null;removeAllViews();LinearLayout p=column();p.setPadding(dp(10),dp(10),dp(10),dp(14));addView(p);
        LinearLayout heading=new LinearLayout(activity);heading.setGravity(Gravity.CENTER_VERTICAL);TextView title=text(title(state.optString("kind")),22,TEXT);title.setTypeface(null,Typeface.BOLD);heading.addView(title,new LinearLayout.LayoutParams(0,-2,1));
        Button collapse=button("×",()->{hidden=state.optString("id");setVisibility(GONE);layoutChanged.accept(false);},true);collapse.setContentDescription("Thu gọn game");heading.addView(collapse,new LinearLayout.LayoutParams(dp(40),dp(40)));p.addView(heading);p.addView(text("BẠN × "+view.optString("peerName"),11,MUTED));
        String status=state.optString("status"),turn=state.optString("turn");
        String label=finished()?"cancelled".equals(status)?"Ván đã hủy":"declined".equals(status)?"Đã từ chối":"".equals(state.optString("winner"))?"HAI BÊN HÒA":me().equals(state.optString("winner"))?"BẠN ĐÃ THẮNG!":"ĐỐI THỦ THẮNG":"placing".equals(status)?"XẾP ĐỘI TÀU":"verifying".equals(status)?"KIỂM CHỨNG ĐỘI TÀU":state.optInt("pendingCell",-1)>=0?"CHỜ KẾT QUẢ BẮN":turn.equals(me())?"ĐẾN LƯỢT BẠN":"ĐỐI THỦ ĐANG NGHĨ…";
        TextView badge=text(label,13,ACCENT);badge.setTypeface(null,Typeface.BOLD);p.addView(badge);
        if(!view.optBoolean("connected"))p.addView(text("Đối thủ offline · Ván đã lưu",11,MUTED));if(pending())p.addView(text("Đang chờ xác nhận…",11,MUTED));
        boolean playable=!pending()&&!finished()&&turn.equals(me());String kind=state.optString("kind");
        if("words".equals(kind))words(p,playable);else if("caro".equals(kind)){p.addView(text("Nối từ 5 quân · Kéo ngang để xem bàn",11,MUTED));board(p,15,ints(state.optJSONArray("board"),225),false,playable,i->action("move",i,"",null));}else ships(p,playable);
        error=text(lastError.isEmpty()?view.optString("error"):lastError,12,CORAL);p.addView(error);
        if(finished()){p.addView(text(state.optString("reason"),12,MUTED));p.addView(button("Tái đấu",()->inviteKind(kind),view.optBoolean("connected")));}
        else{
            String offer=state.optString("drawOffer");if(!offer.isEmpty()&&!offer.equals(me())){p.addView(text("Đối thủ đề nghị hòa",12,ACCENT));p.addView(button("Đồng ý hòa",()->action("draw"),!pending()));p.addView(button("Chơi tiếp",()->action("draw-decline"),!pending()));}
            p.addView(button("Xin hòa",()->action("draw"),!pending()&&offer.isEmpty()));p.addView(button("Đầu hàng",()->showDialog(new AlertDialog.Builder(activity).setMessage("Kết thúc ván và nhận thua?").setNegativeButton("Chơi tiếp",null).setPositiveButton("Đầu hàng",(d,w)->action("resign"))),!pending()));
        }
    }
    private void words(LinearLayout p,boolean playable){
        JSONArray words=state.optJSONArray("words");p.addView(text("CHUỖI NỐI · "+words.length()+" TỪ",11,MUTED));
        for(int i=Math.max(0,words.length()-6);i<words.length();i++){
            String[] parts=words.optString(i).split(" ");LinearLayout row=new LinearLayout(activity);row.setGravity((i%2==0?state.optString("starter"):state.optString("starter").equals(me())?other():me()).equals(me())?Gravity.RIGHT:Gravity.LEFT);
            for(int n=0;n<parts.length;n++){TextView token=text(parts[n],17,n==1?ACCENT:TEXT);token.setTypeface(null,Typeface.BOLD);token.setBackground(shape(n==1?0xff1d4945:SURFACE));token.setPadding(dp(7),dp(9),dp(7),dp(9));LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(-2,-2);lp.setMargins(dp(2),dp(3),dp(2),dp(3));row.addView(token,lp);}
            HorizontalScrollView clip=new HorizontalScrollView(activity);clip.setFillViewport(true);clip.addView(row);p.addView(clip);if(i==words.length()-1){clip.setAlpha(.2f);clip.animate().alpha(1).setDuration(240).start();}
            if(i<words.length()-1){TextView arrow=text("↳",12,MUTED);arrow.setPadding(0,0,0,0);p.addView(arrow);}
        }
        if(words.length()>6)p.addView(button("Xem toàn bộ chuỗi",()->{StringBuilder all=new StringBuilder();for(int i=0;i<words.length();i++){if(i>0)all.append(" → ");all.append(words.optString(i));}showDialog(new AlertDialog.Builder(activity).setTitle("Chuỗi nối chữ").setMessage(all).setPositiveButton("Đóng",null));},true));
        String proposed=state.optString("proposedWord");if(!proposed.isEmpty()){p.addView(text("Đề nghị công nhận:\n“"+proposed+"”",16,TEXT));if(!state.optString("turn").equals(me())){p.addView(button("Có nghĩa · Công nhận",()->action("word-approve"),!pending()));p.addView(button("Không chấp nhận",()->action("word-reject"),!pending()));}else p.addView(text("Chờ đối thủ công nhận",12,MUTED));return;}
        if(finished())return;String prefix=words.length()==0?"":words.optString(words.length()-1).split(" ")[1];if(!prefix.isEmpty()){TextView token=text(prefix+"  🔒",23,ACCENT);token.setTypeface(null,Typeface.BOLD);p.addView(token);}
        wordInput=new EditText(activity);wordInput.setText(draft);wordInput.setTextColor(TEXT);wordInput.setHintTextColor(MUTED);wordInput.setTextSize(18);wordInput.setHint(prefix.isEmpty()?"Ví dụ: học sinh":"Tiếng tiếp theo…");wordInput.setContentDescription("Nhập từ nối");wordInput.setSingleLine();wordInput.setImeOptions(android.view.inputmethod.EditorInfo.IME_ACTION_DONE);wordInput.setEnabled(playable);wordInput.setFilters(new InputFilter[]{new InputFilter.LengthFilter(prefix.isEmpty()?40:16)});p.addView(wordInput);
        Runnable send=()->action("word",-1,(prefix.isEmpty()?"":prefix+" ")+wordInput.getText(),null);p.addView(button("Nối từ →",send,playable));wordInput.setOnEditorActionListener((v,id,event)->{if(id==android.view.inputmethod.EditorInfo.IME_ACTION_DONE){send.run();return true;}return false;});
        p.addView(text("Từ chưa có trong từ điển sẽ được gửi để đối thủ công nhận. Không lặp từ đã dùng.",11,MUTED));
    }
    private void ships(LinearLayout p,boolean playable){
        JSONObject fleets=state.optJSONObject("fleets");
        if("placing".equals(state.optString("status"))&&!fleets.has(me())){
            p.addView(text("Chọn tàu, hướng rồi chạm ô đầu tàu. Đội tàu: 5–4–3–3–2 ô.",11,MUTED));
            Spinner choice=new Spinner(activity);String[] names={"Tàu 1 · 5 ô","Tàu 2 · 4 ô","Tàu 3 · 3 ô","Tàu 4 · 3 ô","Tàu 5 · 2 ô"};ArrayAdapter<String> adapter=new ArrayAdapter<>(activity,android.R.layout.simple_spinner_dropdown_item,names);choice.setAdapter(adapter);choice.setSelection(selectedShip-1);choice.setOnItemSelectedListener(new android.widget.AdapterView.OnItemSelectedListener(){public void onItemSelected(android.widget.AdapterView<?> a,View v,int pos,long id){selectedShip=pos+1;}public void onNothingSelected(android.widget.AdapterView<?> a){}});p.addView(choice);
            p.addView(button(vertical?"↕ Đặt dọc":"↔ Đặt ngang",()->{vertical=!vertical;render();},!pending()));p.addView(button("Xếp tự động",()->{fleet=randomFleet();render();},!pending()));
            int[] shown=fleet.clone();for(int i=0;i<100;i++)shown[i]=shown[i]==0?0:3;
            board(p,10,shown,true,!pending(),cell->{int len=LENGTHS[selectedShip-1];if((vertical?cell/10:cell%10)+len>10){toast("Tàu vượt mép bàn");return;}for(int i=0;i<len;i++){int at=cell+i*(vertical?10:1);if(fleet[at]!=0&&fleet[at]!=selectedShip){toast("Tàu bị chồng lên nhau");return;}}for(int i=0;i<100;i++)if(fleet[i]==selectedShip)fleet[i]=0;for(int i=0;i<len;i++)fleet[cell+i*(vertical?10:1)]=selectedShip;render();});
            p.addView(button("Khóa đội tàu · Sẵn sàng",()->action("fleet",-1,"",fleet),!pending()));return;
        }
        p.addView(button(own?"Xem vùng bắn đối thủ":"Xem đội tàu của tôi",()->{own=!own;render();},true));p.addView(text(own?"ĐỘI TÀU CỦA BẠN":"VÙNG BẮN ĐỐI THỦ",11,MUTED));
        int[] cells=new int[100];if(own){int[] mine=ints(view.optJSONArray("ownBoard"),100);for(int i=0;i<100;i++)if(mine[i]>0)cells[i]=3;}
        JSONObject shots=state.optJSONObject("shots").optJSONObject(own?me():other());int hits=0;if(shots!=null){Iterator<String> keys=shots.keys();while(keys.hasNext()){String key=keys.next();int i=Integer.parseInt(key);cells[i]=shots.optInt(key)>0?5:4;if(cells[i]==5)hits++;}}
        board(p,10,cells,true,playable&&!own&&"playing".equals(state.optString("status"))&&state.optInt("pendingCell",-1)<0,i->action("shot",i,"",null));p.addView(text("Trúng "+hits+"/17 ô",12,MUTED));
    }
    private static int[] ints(JSONArray array,int size){int[] values=new int[size];if(array!=null)for(int i=0;i<Math.min(size,array.length());i++)values[i]=array.optInt(i);return values;}
    private static int[] randomFleet(){Random random=new java.security.SecureRandom();int[] board=new int[100];for(int ship=1;ship<=5;ship++){while(true){int cell=random.nextInt(100),step=random.nextBoolean()?10:1,len=LENGTHS[ship-1];if((step==10?cell/10:cell%10)+len>10)continue;boolean free=true;for(int i=0;i<len;i++)if(board[cell+i*step]!=0)free=false;if(!free)continue;for(int i=0;i<len;i++)board[cell+i*step]=ship;break;}}return board;}
    private void board(LinearLayout parent,int size,int[] cells,boolean ships,boolean playable,IntConsumer click){
        HorizontalScrollView scroller=new HorizontalScrollView(activity);Board board=new Board(size,cells,ships,playable,click);scroller.addView(board,new HorizontalScrollView.LayoutParams(dp(330),dp(330)));parent.addView(scroller);parent.addView(text("Kéo ngang để xem bàn · Có thể xoay ngang điện thoại",10,MUTED));
        SeekBar zoom=new SeekBar(activity);zoom.setMax(150);zoom.setProgress(30);zoom.setContentDescription("Phóng to bàn chơi");zoom.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){public void onProgressChanged(SeekBar bar,int value,boolean user){if(user){int width=dp(300+value*2);board.setLayoutParams(new HorizontalScrollView.LayoutParams(width,width));}}public void onStartTrackingTouch(SeekBar b){}public void onStopTrackingTouch(SeekBar b){}});parent.addView(zoom);
    }
    private final class Board extends View {
        final int size;final int[] cells;final boolean ships,playable;final IntConsumer click;final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG);float downX,downY;
        Board(int size,int[] cells,boolean ships,boolean playable,IntConsumer click){super(activity);this.size=size;this.cells=cells;this.ships=ships;this.playable=playable;this.click=click;setContentDescription(ships?"Bàn bắn tàu":"Bàn caro");setFocusable(true);}
        protected void onMeasure(int widthSpec,int heightSpec){int preferred=getLayoutParams()!=null&&getLayoutParams().width>0?getLayoutParams().width:dp(330);setMeasuredDimension(resolveSize(preferred,widthSpec),resolveSize(preferred,heightSpec));}
        protected void onDraw(Canvas c){super.onDraw(c);float u=getWidth()/(float)size;c.drawColor(SURFACE);for(int i=0;i<cells.length;i++){float x=(i%size)*u,y=(i/size)*u,cx=x+u/2,cy=y+u/2,r=u*.27f;paint.setColor(0xff37505b);paint.setStyle(Paint.Style.STROKE);paint.setStrokeWidth(dp(.7f));c.drawRect(x,y,x+u,y+u,paint);int v=cells[i];paint.setStrokeWidth(dp(2));if(ships){paint.setStyle(Paint.Style.FILL);if(v==3){paint.setColor(ACCENT);c.drawRoundRect(x+3,y+3,x+u-3,y+u-3,4,4,paint);}else if(v==4||v==5){paint.setColor(v==5?CORAL:MUTED);c.drawCircle(cx,cy,v==5?r:u*.08f,paint);}}else if(v==1){paint.setColor(ACCENT);c.drawLine(cx-r,cy-r,cx+r,cy+r,paint);c.drawLine(cx+r,cy-r,cx-r,cy+r,paint);}else if(v==2){paint.setColor(CORAL);c.drawCircle(cx,cy,r,paint);}}}
        public boolean onTouchEvent(android.view.MotionEvent e){if(!playable)return false;if(e.getAction()==MotionEvent.ACTION_DOWN){downX=e.getX();downY=e.getY();return true;}if(e.getAction()==MotionEvent.ACTION_UP&&Math.abs(e.getX()-downX)<dp(8)&&Math.abs(e.getY()-downY)<dp(8)){performClick();int x=(int)(e.getX()*size/getWidth()),y=(int)(e.getY()*size/getHeight());if(x>=0&&x<size&&y>=0&&y<size)click.accept(y*size+x);return true;}return true;}
        public boolean performClick(){super.performClick();return true;}
    }
}
