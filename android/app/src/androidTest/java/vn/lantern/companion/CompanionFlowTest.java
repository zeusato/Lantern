package vn.lantern.companion;

import android.content.*;
import android.widget.*;
import androidx.test.core.app.ActivityScenario;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.function.BooleanSupplier;
import static org.junit.Assert.*;
import static androidx.test.espresso.Espresso.*;
import static androidx.test.espresso.action.ViewActions.*;
import static androidx.test.espresso.assertion.ViewAssertions.*;
import static androidx.test.espresso.matcher.ViewMatchers.*;
import static org.hamcrest.Matchers.not;

@RunWith(AndroidJUnit4.class)
public class CompanionFlowTest {
    private static void until(BooleanSupplier condition)throws Exception{long deadline=System.currentTimeMillis()+25000;while(!condition.getAsBoolean()){if(System.currentTimeMillis()>deadline)throw new AssertionError("Timed out waiting for companion state");Thread.sleep(50);}}
    private static androidx.test.espresso.ViewAction pendingClick(boolean repeat){return new androidx.test.espresso.ViewAction(){public org.hamcrest.Matcher<android.view.View> getConstraints(){return isAssignableFrom(Button.class);}public String getDescription(){return "Click once and verify pending state before response arrives";}public void perform(androidx.test.espresso.UiController ui,android.view.View view){view.performClick();assertFalse("Pending action must disable repeated clicks",view.isEnabled());if(repeat)for(int i=0;i<8;i++)view.performClick();}};}
    @Test public void encryptedCacheProtectsLocksExpiryAndRetryIdentity()throws Exception{
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();DeviceKeys.ensure();HistoryCache.clearAll(context);
        HistoryCache cache=new HistoryCache(context,"A".repeat(64));String conversation="c".repeat(64);
        cache.saveConversations(new JSONArray().put(new JSONObject().put("id",conversation).put("protected",true).put("locked",false)).put(new JSONObject().put("id","public").put("protected",false).put("locked",false)));
        assertTrue(cache.conversations().getJSONObject(0).getBoolean("locked"));assertFalse(cache.conversations().getJSONObject(1).getBoolean("locked"));
        cache.saveMessages(conversation,new JSONArray().put(new JSONObject().put("text","cache-private-canary")).put(new JSONObject().put("text","ephemeral-canary").put("disappearAfterSeconds",60)));
        assertEquals(1,cache.messages(conversation).length());assertEquals("cache-private-canary",new HistoryCache(context,"A".repeat(64)).messages(conversation).getJSONObject(0).getString("text"));assertEquals(0,new HistoryCache(context,"B".repeat(64)).messages(conversation).length());
        for(File f:new File(context.getFilesDir(),"history-cache").listFiles())assertFalse(new String(Files.readAllBytes(f.toPath()),StandardCharsets.UTF_8).contains("cache-private-canary"));
        String first=cache.sendId(conversation,"Hello","");assertEquals(first,new HistoryCache(context,"A".repeat(64)).sendId(conversation,"Hello",""));cache.confirmSend(conversation,first);assertNotEquals(first,cache.sendId(conversation,"Hello",""));
        HistoryCache.clearAll(context);assertEquals(0,cache.messages(conversation).length());assertThrows(IOException.class,()->cache.saveMessages(conversation,new JSONArray().put(new JSONObject().put("text","stale writer"))));
    }
    @Test public void unlockAndSendStayResponsiveDuringSlowImageDownload()throws Exception{
        String pin=InstrumentationRegistry.getArguments().getString("pcPin");org.junit.Assume.assumeNotNull(pin);Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();
        try(InputStream in=new android.os.ParcelFileDescriptor.AutoCloseInputStream(InstrumentationRegistry.getInstrumentation().getUiAutomation().executeShellCommand("pm grant vn.lantern.companion android.permission.POST_NOTIFICATIONS"))){while(in.read()!=-1){}}
        DeviceKeys.save(context,new JSONObject().put("pc",pin).put("name","QA PC").put("port",45854).put("addresses",new JSONArray().put("10.0.2.2")).put("nonce","q".repeat(43)));
        ConnectionService.selected="";String conversation="a".repeat(64),text="Send during image download";
        try(ActivityScenario<MainActivity> scenario=ActivityScenario.launch(MainActivity.class)){
            until(()->ConnectionService.connected&&ConnectionService.conversations.length()>0);
            onView(withText("QA contact")).perform(click());onView(withText("Mở khóa hội thoại")).perform(click());
            onView(withContentDescription("Mật khẩu hội thoại")).perform(typeText("qa-password"));onView(withText("Mở khóa")).perform(pendingClick(false));
            until(()->ConnectionService.messages.length()>=30);
            onView(withContentDescription("Soạn tin nhắn")).perform(typeText(text));long start=android.os.SystemClock.elapsedRealtime();onView(withContentDescription("Gửi tin nhắn")).perform(click());
            AtomicBoolean cleared=new AtomicBoolean();until(()->{scenario.onActivity(activity->{try{java.lang.reflect.Field field=MainActivity.class.getDeclaredField("composer");field.setAccessible(true);cleared.set(((EditText)field.get(activity)).getText().length()==0);}catch(Exception e){throw new AssertionError(e);}});return cleared.get();});
            assertEquals("Optimistic message must be present before waiting for server history",1,countText(text));
            assertTrue("Composer must clear immediately after a real touch with keyboard open",android.os.SystemClock.elapsedRealtime()-start<1500);
            onView(withContentDescription("Gửi tin nhắn")).perform(click(),click(),click());
            until(()->acknowledged(text));assertTrue("PC acknowledgement must arrive within 2 seconds during 8 MB preview",android.os.SystemClock.elapsedRealtime()-start<2000);
            until(()->countText(text)==1);assertEquals(1,countText(text));
            scenario.onActivity(activity->{ConnectionService.current.select("");ConnectionService.current.select(conversation);assertEquals("Cached history should be immediately available",1,countText(text));});
            scenario.moveToState(androidx.lifecycle.Lifecycle.State.CREATED);Thread.sleep(1600);scenario.moveToState(androidx.lifecycle.Lifecycle.State.RESUMED);until(()->countText(text)==1);
            context.stopService(new Intent(context,ConnectionService.class));until(()->ConnectionService.current==null);
            assertTrue(ConnectionService.conversations.getJSONObject(0).getBoolean("locked"));assertEquals(0,ConnectionService.messages.length());
        }finally{context.stopService(new Intent(context,ConnectionService.class));until(()->ConnectionService.current==null);DeviceKeys.forget(context);ConnectionService.selected="";ConnectionService.conversations=new JSONArray();ConnectionService.messages=new JSONArray();}
    }
    private static int countText(String text){JSONArray messages=ConnectionService.messages;int count=0;for(int i=0;i<messages.length();i++)if(text.equals(messages.optJSONObject(i).optString("text")))count++;return count;}
    private static boolean acknowledged(String text){JSONArray messages=ConnectionService.messages;for(int i=0;i<messages.length();i++){JSONObject m=messages.optJSONObject(i);if(text.equals(m.optString("text"))&&!m.has("localRequest"))return true;}return false;}
}
