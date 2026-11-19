package vn.lantern.companion;

import android.content.Context;
import android.graphics.*;
import android.util.Base64;
import android.view.*;
import android.widget.*;
import androidx.test.core.app.ActivityScenario;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.json.*;
import java.io.*;
import java.lang.reflect.*;
import java.util.*;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public class ChatMediaTest {
    private static <T extends View> List<T> views(View root,Class<T> type){List<T> found=new ArrayList<>();if(type.isInstance(root))found.add(type.cast(root));if(root instanceof ViewGroup group)for(int i=0;i<group.getChildCount();i++)found.addAll(views(group.getChildAt(i),type));return found;}
    private static Object field(Object object,String name)throws Exception{Field f=object.getClass().getDeclaredField(name);f.setAccessible(true);return f.get(object);}
    @Test public void stickersAnimateWithoutFileCardsAndIncomingAuthorsHaveOneAvatar()throws Exception{
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();DeviceKeys.forget(context);ConnectionService.selected="";
        File gif=new File(context.getCacheDir(),"qa-sticker.gif");try(InputStream in=InstrumentationRegistry.getInstrumentation().getContext().getAssets().open("sticker.gif");OutputStream out=new FileOutputStream(gif)){in.transferTo(out);}
        Bitmap photo=Bitmap.createBitmap(192,192,Bitmap.Config.ARGB_8888);photo.eraseColor(Color.BLUE);ByteArrayOutputStream bytes=new ByteArrayOutputStream();photo.compress(Bitmap.CompressFormat.PNG,100,bytes);photo.recycle();String avatar=Base64.encodeToString(bytes.toByteArray(),Base64.NO_WRAP);
        try(ActivityScenario<MainActivity> scenario=ActivityScenario.launch(MainActivity.class)){
            scenario.onActivity(activity->{try{
                ConnectionService.selected="a".repeat(64);ConnectionService.conversations=new JSONArray().put(new JSONObject().put("id",ConnectionService.selected).put("name","QA").put("isGroup",true).put("locked",false));ConnectionService.messages=new JSONArray();
                for(int i=0;i<3;i++){String id=String.valueOf(i+1).repeat(32);ConnectionService.images.put(id,gif);JSONObject message=new JSONObject().put("id",id).put("conversationId",ConnectionService.selected).put("senderId",i==2?"b".repeat(64):ConnectionService.selected).put("senderName","Người gửi thử").put("outgoing",i==2).put("kind","image").put("fileName","qa-sticker.gif").put("sticker",new JSONObject().put("name","Ngủ ngon")).put("status","received").put("time","2026-11-19T10:00:00+07:00");if(i==1)message.put("senderAvatar",avatar);ConnectionService.messages.put(message);}
                Method build=MainActivity.class.getDeclaredMethod("build");build.setAccessible(true);build.invoke(activity);
            }catch(Exception e){throw new AssertionError(e);}});
            long deadline=System.currentTimeMillis()+5000;boolean[] ready={false};
            do {Thread.sleep(60);scenario.onActivity(activity->{try{List<StickerView> images=views((View)field(activity,"messagesBox"),StickerView.class);ready[0]=images.size()==3;for(StickerView view:images)ready[0]&=field(view,"movie")!=null;}catch(Exception e){throw new AssertionError(e);}});}while(!ready[0]&&System.currentTimeMillis()<deadline);
            assertTrue("GIFs decode as animations",ready[0]);
            scenario.onActivity(activity->{try{
                View root=(View)field(activity,"messagesBox");List<StickerView> images=views(root,StickerView.class);assertEquals(3,images.size());
                for(StickerView view:images){assertNull("Sticker bubble stays transparent",((View)view.getParent()).getBackground());assertEquals((int)(160*activity.getResources().getDisplayMetrics().density),view.getLayoutParams().width);}
                assertEquals(1,views(root,ImageView.class).stream().filter(v->"Ảnh đại diện Người gửi thử".contentEquals(v.getContentDescription()==null?"":v.getContentDescription())).count());
                assertEquals(1,views(root,TextView.class).stream().filter(v->"Người gửi thử".contentEquals(v.getText())).count());
                assertFalse(views(root,TextView.class).stream().anyMatch(v->v.getText().toString().contains("qa-sticker.gif")||"Mở tệp".contentEquals(v.getText())));
                CompanionTest.snapshot(activity,"android-stickers-avatars.png");
            }catch(Exception e){throw new AssertionError(e);}});
        }finally{ConnectionService.selected="";ConnectionService.messages=new JSONArray();ConnectionService.conversations=new JSONArray();ConnectionService.images.clear();gif.delete();}
    }
}
