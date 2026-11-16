package vn.lantern.companion;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.view.View;
import androidx.test.core.app.ActivityScenario;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.json.*;
import java.io.*;
import java.lang.reflect.Method;
import java.util.UUID;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public class CompanionTest {
    @Test public void keysAndPrivateConfiguration() throws Exception {
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();DeviceKeys.ensure();String id=DeviceKeys.id();assertTrue(id.matches("[A-F0-9]{64}"));assertEquals(id,DeviceKeys.id());
        JSONObject value=new JSONObject().put("test","private-token-canary");DeviceKeys.save(context,value);assertEquals("private-token-canary",DeviceKeys.load(context).getString("test"));
        assertFalse(context.getSharedPreferences("private",0).getString("connection","").contains("private-token-canary"));DeviceKeys.forget(context);assertNull(DeviceKeys.load(context));
    }
    @Test public void qrRejectsExpiredAndUntrustedSchemes() throws Exception {
        JSONObject config=new JSONObject().put("v",1).put("pc","A".repeat(64)).put("nonce","a".repeat(43)).put("expires",System.currentTimeMillis()/1000+100).put("port",45854).put("addresses",new JSONArray().put("192.168.1.2"));
        assertEquals(1,PcConnection.validateQr(config.toString()).getInt("v"));config.put("expires",1);assertThrows(IOException.class,()->PcConnection.validateQr(config.toString()));
        config.put("expires",System.currentTimeMillis()/1000+100).put("relay",new JSONObject().put("url","http://example.com/").put("id","a".repeat(32)).put("key","b".repeat(43)).put("token","secret"));assertThrows(IOException.class,()->PcConnection.validateQr(config.toString()));
    }
    @Test public void chatLayoutAndLockedConversation() throws Exception {
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();DeviceKeys.forget(context);ConnectionService.selected="";
        try(ActivityScenario<MainActivity> scenario=ActivityScenario.launch(MainActivity.class)){
            scenario.onActivity(activity->{
                try{
                    snapshot(activity,"android-welcome.png");
                    ConnectionService.selected="a".repeat(64);ConnectionService.status="Đã kết nối · PC thử nghiệm";
                    ConnectionService.conversations=new JSONArray().put(new JSONObject().put("id",ConnectionService.selected).put("name","Minh Anh").put("locked",false));
                    ConnectionService.messages=new JSONArray().put(message("OK",false)).put(message("Em nhận rồi 👍",true)).put(message("Ảnh và tệp sẽ đi qua PC bằng kênh mã hóa riêng.",false));
                    Method build=MainActivity.class.getDeclaredMethod("build");build.setAccessible(true);build.invoke(activity);activity.getWindow().getDecorView().measure(View.MeasureSpec.makeMeasureSpec(1080,View.MeasureSpec.EXACTLY),View.MeasureSpec.makeMeasureSpec(1920,View.MeasureSpec.EXACTLY));activity.getWindow().getDecorView().layout(0,0,1080,1920);snapshot(activity,"android-chat.png");
                    ConnectionService.conversations.getJSONObject(0).put("locked",true);build.invoke(activity);snapshot(activity,"android-locked.png");
                    java.lang.reflect.Field field=MainActivity.class.getDeclaredField("composerBar");field.setAccessible(true);assertEquals(View.GONE,((View)field.get(activity)).getVisibility());
                }catch(Exception e){throw new AssertionError(e);}
            });
        }finally{ConnectionService.selected="";ConnectionService.conversations=new JSONArray();ConnectionService.messages=new JSONArray();}
    }
    private static JSONObject message(String text,boolean outgoing)throws Exception{return new JSONObject().put("id",UUID.randomUUID().toString().replace("-","")).put("kind","text").put("text",text).put("outgoing",outgoing).put("senderName","Minh Anh").put("time","2026-11-16T10:00:00+07:00").put("status","delivered");}
    private static void snapshot(MainActivity activity,String name)throws Exception{
        View view=activity.getWindow().getDecorView();int width=Math.max(1,view.getWidth()),height=Math.max(1,view.getHeight());Bitmap bitmap=Bitmap.createBitmap(width,height,Bitmap.Config.ARGB_8888);view.draw(new Canvas(bitmap));
        try(FileOutputStream out=new FileOutputStream(new File(activity.getExternalFilesDir(null),name))){bitmap.compress(Bitmap.CompressFormat.PNG,100,out);}bitmap.recycle();
    }
}
