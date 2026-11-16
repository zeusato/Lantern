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
import java.util.concurrent.atomic.AtomicLong;
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
    @Test public void outboxSurvivesRestartAndExcludesExpiringText()throws Exception{
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();DeviceKeys.ensure();HistoryCache.clearAll(context);HistoryCache cache=new HistoryCache(context,"C".repeat(64));
        cache.saveOutbox(new JSONArray().put(new JSONObject().put("id","stable-request").put("text","pending-canary")).put(new JSONObject().put("text","ephemeral-canary").put("disappearAfterSeconds",10)));
        JSONArray restored=new HistoryCache(context,"C".repeat(64)).outbox();assertEquals(1,restored.length());assertEquals("stable-request",restored.getJSONObject(0).getString("id"));assertEquals(0,new HistoryCache(context,"D".repeat(64)).outbox().length());
        for(File f:new File(context.getFilesDir(),"history-cache").listFiles())assertFalse(new String(Files.readAllBytes(f.toPath()),StandardCharsets.UTF_8).contains("pending-canary"));
        HistoryCache.clearAll(context);assertEquals(0,cache.outbox().length());
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
            onView(withContentDescription("Soạn tin nhắn")).perform(typeText(text));
            // Measure Android's actual touch/clear/receive events, excluding Espresso's idle waits.
            AtomicLong pressedAt=new AtomicLong(),renderedAt=new AtomicLong(),ackAt=new AtomicLong(),incomingAt=new AtomicLong();AtomicBoolean localPresent=new AtomicBoolean();
            scenario.onActivity(activity->{try{
                java.lang.reflect.Field field=MainActivity.class.getDeclaredField("composer");field.setAccessible(true);EditText editor=(EditText)field.get(activity);
                editor.addTextChangedListener(new android.text.TextWatcher(){public void beforeTextChanged(CharSequence s,int start,int count,int after){}public void onTextChanged(CharSequence s,int start,int before,int count){}public void afterTextChanged(android.text.Editable s){if(s.length()==0&&pressedAt.get()>0){localPresent.set(countText(text)==1);editor.post(()->renderedAt.compareAndSet(0,android.os.SystemClock.elapsedRealtime()));}}});
                field=MainActivity.class.getDeclaredField("sendButton");field.setAccessible(true);((Button)field.get(activity)).setOnTouchListener((v,event)->{if(event.getAction()==android.view.MotionEvent.ACTION_UP)pressedAt.compareAndSet(0,android.os.SystemClock.elapsedRealtime());return false;});
            }catch(Exception e){throw new AssertionError(e);}});
            Thread observer=new Thread(()->{long deadline=android.os.SystemClock.elapsedRealtime()+15000;while(android.os.SystemClock.elapsedRealtime()<deadline&&(ackAt.get()==0||incomingAt.get()==0)){if(pressedAt.get()>0){if(acknowledged(text))ackAt.compareAndSet(0,android.os.SystemClock.elapsedRealtime());if(countText("Incoming immediately after send")==1)incomingAt.compareAndSet(0,android.os.SystemClock.elapsedRealtime());}try{Thread.sleep(5);}catch(InterruptedException e){return;}}});observer.setDaemon(true);observer.start();
            onView(withContentDescription("Gửi tin nhắn")).perform(click());until(()->renderedAt.get()>0&&ackAt.get()>0&&incomingAt.get()>0);observer.join(1000);
            long localMs=renderedAt.get()-pressedAt.get(),ackMs=ackAt.get()-pressedAt.get(),incomingMs=incomingAt.get()-pressedAt.get();android.util.Log.i("LanternQA","send local_ms="+localMs+" ack_ms="+ackMs+" incoming_ms="+incomingMs);
            assertTrue("Optimistic message must exist when composer clears",localPresent.get());assertTrue("Local render took "+localMs+" ms",localMs<1000);
            assertTrue("PC acknowledgement took "+ackMs+" ms during 8 MB preview",ackMs<2000);assertTrue("Incoming push took "+incomingMs+" ms during 8 MB preview",incomingMs<2000);
            onView(withContentDescription("Gửi tin nhắn")).perform(click(),click(),click());
            until(()->countText(text)==1);assertEquals(1,countText(text));
            // Never tap/refocus the editor: continue typing through the existing input connection
            // after send, acknowledgement and an incoming push all rebuild the message list.
            String next="Next message without tapping the input";
            onView(withContentDescription("Soạn tin nhắn")).check(matches(hasFocus())).perform(typeTextIntoFocusedView(next)).check(matches(withText(next)));
            onView(withContentDescription("Gửi tin nhắn")).check(matches(isCompletelyDisplayed()));
            scenario.onActivity(activity->{try{
                java.lang.reflect.Field field=MainActivity.class.getDeclaredField("composer");field.setAccessible(true);EditText editor=(EditText)field.get(activity);
                assertTrue("Composer must stay the active IME target",((android.view.inputmethod.InputMethodManager)activity.getSystemService(Context.INPUT_METHOD_SERVICE)).isActive(editor));
                assertNotNull(editor.getLayout());assertEquals("Typed text must already be laid out",next,editor.getLayout().getText().toString());
                CompanionTest.snapshotCurrent(activity,"android-composer-after-send.png");
            }catch(Exception e){throw new AssertionError(e);}});
            onView(withContentDescription("Gửi tin nhắn")).perform(click());until(()->acknowledged(next));
            assertEquals(1,countText(next));
            onView(withContentDescription("Soạn tin nhắn")).check(matches(hasFocus())).perform(typeTextIntoFocusedView("Draft after second send")).check(matches(withText("Draft after second send")));
            scenario.onActivity(activity->{ConnectionService.current.select("");ConnectionService.current.select(conversation);assertEquals("Cached history should be immediately available",1,countText(text));});
            scenario.moveToState(androidx.lifecycle.Lifecycle.State.CREATED);Thread.sleep(1600);scenario.moveToState(androidx.lifecycle.Lifecycle.State.RESUMED);until(()->countText(text)==1);
            scenario.onActivity(activity->{try{CompanionTest.snapshotCurrent(activity,"android-live-chat.png");}catch(Exception e){throw new AssertionError(e);}});
            context.stopService(new Intent(context,ConnectionService.class));until(()->ConnectionService.current==null);
            assertTrue(ConnectionService.conversations.getJSONObject(0).getBoolean("locked"));assertEquals(0,ConnectionService.messages.length());
        }finally{context.stopService(new Intent(context,ConnectionService.class));until(()->ConnectionService.current==null);DeviceKeys.forget(context);ConnectionService.selected="";ConnectionService.conversations=new JSONArray();ConnectionService.messages=new JSONArray();}
    }
    private static int countText(String text){JSONArray messages=ConnectionService.messages;int count=0;for(int i=0;i<messages.length();i++)if(text.equals(messages.optJSONObject(i).optString("text")))count++;return count;}
    private static boolean acknowledged(String text){JSONArray messages=ConnectionService.messages;for(int i=0;i<messages.length();i++){JSONObject m=messages.optJSONObject(i);if(text.equals(m.optString("text"))&&!m.has("localRequest"))return true;}return false;}
}
