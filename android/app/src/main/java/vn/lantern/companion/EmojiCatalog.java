package vn.lantern.companion;

import android.app.AlertDialog;
import android.content.Context;
import android.graphics.drawable.Drawable;
import android.text.*;
import android.text.style.ImageSpan;
import android.view.Gravity;
import android.view.inputmethod.BaseInputConnection;
import android.widget.*;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.regex.*;

/** Uses the exact PC catalog and artwork. Spans retain the original copyable text. */
final class EmojiCatalog {
    record Entry(String token,String name,String file,String shortcode,String category,List<String> aliases){}
    private static EmojiCatalog instance;
    private final Context context;
    final List<Entry> entries=new ArrayList<>();
    private final Map<String,Entry> tokens=new HashMap<>();
    private final Map<String,String> replacements=new HashMap<>();
    private final android.util.LruCache<String,Drawable.ConstantState> pictures=new android.util.LruCache<>(160);
    private static final Pattern LITERAL=Pattern.compile("```[\\s\\S]*?(?:```|$)|`[^`\\r\\n]*(?:`|$)|(?:https?://|www\\.|[a-z]:\\\\)\\S+",Pattern.CASE_INSENSITIVE);
    private static final class TokenNode {final Map<Character,TokenNode> next=new HashMap<>();boolean terminal,ascii;}
    private record Hit(int start,int end,String text){}
    private final TokenNode root=new TokenNode();
    static synchronized EmojiCatalog get(Context c){if(instance==null)try{instance=new EmojiCatalog(c.getApplicationContext());}catch(Exception e){throw new IllegalStateException("Không đọc được thư viện emoji",e);}return instance;}
    private String read(String file)throws IOException{try(InputStream input=context.getAssets().open(file)){ByteArrayOutputStream bytes=new ByteArrayOutputStream();byte[] buffer=new byte[8192];int count;while((count=input.read(buffer))!=-1)bytes.write(buffer,0,count);return new String(bytes.toByteArray(),StandardCharsets.UTF_8);}}
    private EmojiCatalog(Context c)throws Exception{
        context=c;JSONArray catalog=new JSONArray(read("zalo-catalog.json"));
        for(int i=0;i<catalog.length();i++){
            JSONObject p=catalog.getJSONObject(i);List<String> aliases=new ArrayList<>();JSONArray list=p.optJSONArray("Aliases");if(list!=null)for(int j=0;j<list.length();j++)aliases.add(list.getString(j));
            Entry item=new Entry(p.getString("Emoji"),p.getString("Name"),p.getString("File"),p.optString("Shortcode",p.getString("Emoji")),p.getString("Category"),aliases);entries.add(item);
            List<String> keys=new ArrayList<>(aliases);keys.add(item.token());keys.add(item.shortcode());for(String key:keys){tokens.putIfAbsent(key.toLowerCase(Locale.ROOT),item);tokens.putIfAbsent(key.replace("\uFE0F","").toLowerCase(Locale.ROOT),item);}
        }
        JSONObject codes=new JSONObject(read("emoji-shortcodes.json"));Iterator<String> keys=codes.keys();while(keys.hasNext()){String key=keys.next();replacements.put(key.toLowerCase(Locale.ROOT),codes.getString(key));}
        String[][] fallback={{":-)","🙂"},{":-d","😃"},{";)","😉"},{":-(","🙁"},{":-p","😛"},{":-o","😮"},{":'(","😢"},{":-|","😐"},{"<3","❤️"},{"</3","💔"}};
        for(String[] pair:fallback){replacements.putIfAbsent(pair[0],pair[1]);Entry item=tokens.get(pair[1].toLowerCase(Locale.ROOT));if(item!=null)tokens.putIfAbsent(pair[0],item);}
        for(String key:tokens.keySet()){TokenNode node=root;for(int i=0;i<key.length();i++)node=node.next.computeIfAbsent(key.charAt(i),ignored->new TokenNode());node.terminal=true;node.ascii=key.chars().allMatch(n->n<128);}
    }
    // Prefix lookup avoids testing thousands of regex alternatives at every ordinary letter.
    private List<Hit> matches(CharSequence text){
        List<Hit> hits=new ArrayList<>();Matcher literal=LITERAL.matcher(text);boolean hasLiteral=literal.find();
        for(int i=0;i<text.length();){
            if(hasLiteral&&i>=literal.start()){i=literal.end();hasLiteral=literal.find();continue;}
            TokenNode node=root;int end=-1;
            for(int j=i;j<text.length()&&(!hasLiteral||j<literal.start());j++){
                node=node.next.get(Character.toLowerCase(text.charAt(j)));if(node==null)break;
                if(node.terminal&&(!node.ascii||((i==0||Character.isWhitespace(text.charAt(i-1)))&&(j+1==text.length()||Character.isWhitespace(text.charAt(j+1))||"!?,.".indexOf(text.charAt(j+1))>=0))))end=j+1;
            }
            if(end>i){hits.add(new Hit(i,end,text.subSequence(i,end).toString()));i=end;}else i++;
        }return hits;
    }
    String replace(String text,boolean trailing){
        StringBuilder result=new StringBuilder();int last=0;for(Hit hit:matches(text)){
            result.append(text,last,hit.start());result.append(!trailing&&hit.end()==text.length()?hit.text():replacements.getOrDefault(hit.text().toLowerCase(Locale.ROOT),hit.text()));last=hit.end();
        }return result.append(text,last,text.length()).toString();
    }
    Drawable image(Entry item,int size){
        try{
            Drawable.ConstantState state=pictures.get(item.file());if(state==null){try(InputStream input=context.getAssets().open(item.file())){Drawable picture=Drawable.createFromStream(input,item.file());if(picture==null)return null;state=picture.getConstantState();if(state!=null)pictures.put(item.file(),state);else{picture.setBounds(0,0,size,size);return picture;}}}
            Drawable picture=state.newDrawable(context.getResources()).mutate();picture.setBounds(0,0,size,size);return picture;
        }catch(IOException e){return null;}
    }
    CharSequence render(String text,float textSize){
        return render(text,textSize,true);
    }
    CharSequence render(String text,float textSize,boolean enlargeStandalone){
        List<Hit> hits=matches(text);if(hits.isEmpty())return text;
        SpannableStringBuilder result=new SpannableStringBuilder(text);Entry alone=tokens.get(text.trim().toLowerCase(Locale.ROOT));int size=(int)(!enlargeStandalone||alone==null?textSize*1.45f:48*context.getResources().getDisplayMetrics().density);
        for(Hit hit:hits){Entry item=tokens.get(hit.text().toLowerCase(Locale.ROOT));if(item==null)continue;Drawable picture=image(item,size);if(picture!=null)result.setSpan(new ImageSpan(picture,ImageSpan.ALIGN_BOTTOM),hit.start(),hit.end(),Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);}return result;
    }
    private void apply(Spannable text,int size){
        for(ImageSpan span:text.getSpans(0,text.length(),ImageSpan.class))text.removeSpan(span);
        for(Hit hit:matches(text)){Entry item=tokens.get(hit.text().toLowerCase(Locale.ROOT));if(item==null)continue;Drawable picture=image(item,size);if(picture!=null)text.setSpan(new ImageSpan(picture,ImageSpan.ALIGN_BOTTOM),hit.start(),hit.end(),Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);}
    }
    void watch(EditText editor){editor.addTextChangedListener(new TextWatcher(){boolean updating;
        public void beforeTextChanged(CharSequence s,int start,int count,int after){}
        public void onTextChanged(CharSequence s,int start,int before,int count){}
        public void afterTextChanged(Editable value){if(updating||BaseInputConnection.getComposingSpanStart(value)>=0)return;updating=true;try{List<Hit> hits=matches(value);for(int i=hits.size()-1;i>=0;i--){Hit hit=hits.get(i);String replacement=replacements.get(hit.text().toLowerCase(Locale.ROOT));if(hit.end()<value.length()&&replacement!=null&&!replacement.equals(hit.text()))value.replace(hit.start(),hit.end(),replacement);}apply(value,(int)(editor.getTextSize()*1.45f));}finally{updating=false;}}
    });}
    void picker(EditText editor){
        Context activity=editor.getContext();float density=activity.getResources().getDisplayMetrics().density;
        LinearLayout panel=new LinearLayout(activity);panel.setOrientation(LinearLayout.VERTICAL);panel.setPadding(16,8,16,8);
        EditText search=new EditText(activity);search.setSingleLine();search.setHint("Tìm tên hoặc ký tự chèn");search.setContentDescription("Tìm emoji");panel.addView(search);
        Spinner category=new Spinner(activity);List<String> categories=entries.stream().map(Entry::category).distinct().collect(java.util.stream.Collectors.toList());category.setAdapter(new ArrayAdapter<>(activity,android.R.layout.simple_spinner_dropdown_item,categories));panel.addView(category);
        ScrollView scroll=new ScrollView(activity);LinearLayout grid=new LinearLayout(activity);grid.setOrientation(LinearLayout.VERTICAL);scroll.addView(grid);panel.addView(scroll,new LinearLayout.LayoutParams(-1,(int)(300*density)));
        TextView note=new TextView(activity);note.setText("Giữ emoji để xem tên và ký tự chèn. Gõ từ khóa để tìm thêm.");panel.addView(note);
        AlertDialog dialog=new AlertDialog.Builder(activity).setTitle("Emoji").setView(panel).setNegativeButton("Đóng",null).create();
        Runnable render=()->{grid.removeAllViews();String q=searchKey(search.getText().toString());int count=0;LinearLayout row=null;for(Entry item:entries){if(q.isEmpty()?!item.category().equals(category.getSelectedItem()):!searchKey(item.name()+" "+item.shortcode()+" "+String.join(" ",item.aliases())).contains(q))continue;if(count>=120)break;if(count%5==0){row=new LinearLayout(activity);row.setGravity(Gravity.CENTER);grid.addView(row);}ImageButton button=new ImageButton(activity);button.setImageDrawable(image(item,(int)(30*density)));button.setBackgroundColor(android.graphics.Color.TRANSPARENT);button.setContentDescription(item.name()+" · "+item.shortcode());button.setTooltipText(item.name()+"\nKý tự chèn: "+item.shortcode());row.addView(button,new LinearLayout.LayoutParams(0,(int)(48*density),1));button.setOnClickListener(v->{int a=Math.max(0,editor.getSelectionStart()),b=Math.max(a,editor.getSelectionEnd());String token=item.token();if(token.chars().allMatch(n->n<128)){if(a>0&&!Character.isWhitespace(editor.getText().charAt(a-1)))token=" "+token;if(b<editor.length()&&!Character.isWhitespace(editor.getText().charAt(b))&&"!?,.".indexOf(editor.getText().charAt(b))<0)token+=" ";}editor.getText().replace(a,b,token);editor.setSelection(a+token.length());dialog.dismiss();editor.requestFocus();});count++;}note.setText(count==0?"Không tìm thấy emoji.":count==120?"Hiện 120 kết quả. Gõ thêm để thu hẹp tìm kiếm.":"Giữ emoji để xem tên và ký tự chèn.");};
        search.addTextChangedListener(new TextWatcher(){public void beforeTextChanged(CharSequence s,int st,int c,int a){}public void onTextChanged(CharSequence s,int st,int before,int count){render.run();}public void afterTextChanged(Editable s){}});
        category.setOnItemSelectedListener(new android.widget.AdapterView.OnItemSelectedListener(){public void onItemSelected(android.widget.AdapterView<?> p,android.view.View v,int pos,long id){render.run();}public void onNothingSelected(android.widget.AdapterView<?> p){}});dialog.show();render.run();
    }
    private static String searchKey(String value){return java.text.Normalizer.normalize(value.toLowerCase(Locale.ROOT).replace('đ','d'),java.text.Normalizer.Form.NFD).replaceAll("\\p{M}","");}
}
