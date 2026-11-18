package vn.lantern.companion;

import android.content.*;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.Test;
import org.junit.runner.RunWith;
import java.util.function.BooleanSupplier;
import static org.junit.Assert.*;
import static androidx.test.espresso.Espresso.onView;
import static androidx.test.espresso.action.ViewActions.*;
import static androidx.test.espresso.assertion.ViewAssertions.matches;
import static androidx.test.espresso.matcher.ViewMatchers.*;
import static org.hamcrest.Matchers.startsWith;
import static org.hamcrest.Matchers.containsString;

@RunWith(AndroidJUnit4.class)
public class ConversationSettingsTest {
    private static void until(BooleanSupplier test)throws Exception{long end=System.currentTimeMillis()+25000;while(!test.getAsBoolean()){if(System.currentTimeMillis()>end)throw new AssertionError("Conversation state did not arrive");Thread.sleep(50);}}
    private static void openSettings(){onView(withContentDescription("Tùy chọn")).perform(click());onView(withText("Cài đặt hội thoại")).perform(click());}
    @Test public void settingsReachPcAndDeletedHistoryDoesNotReturn()throws Exception{
        String pin=InstrumentationRegistry.getArguments().getString("pcPin");org.junit.Assume.assumeNotNull(pin);
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();context.stopService(new Intent(context,ConnectionService.class));until(()->ConnectionService.current==null);DeviceKeys.forget(context);
        DeviceKeys.save(context,new JSONObject().put("pc",pin).put("name","QA PC").put("port",45854).put("addresses",new JSONArray().put("10.0.2.2")).put("nonce","q".repeat(43)));
        String id="a".repeat(64);ConnectionService.selected="";
        try(ActivityScenario<MainActivity> scenario=ActivityScenario.launch(MainActivity.class)){
            until(()->ConnectionService.connected&&ConnectionService.conversations.length()>0);
            onView(withText("QA contact")).perform(click());onView(withText("Mở khóa hội thoại")).perform(click());
            onView(withContentDescription("Mật khẩu hội thoại")).perform(replaceText("qa-password"),closeSoftKeyboard());onView(withText("Mở khóa")).perform(click());
            until(()->ConnectionService.messages.length()>=30);openSettings();
            onView(withText("Đặt tên riêng")).perform(click());onView(withContentDescription("Tên riêng")).perform(replaceText("QA private name"),closeSoftKeyboard());onView(withText("Lưu")).perform(click());
            until(()->"QA private name".equals(ConversationSettings.contact(id).optString("name")));
            onView(withText(startsWith("Tin nhắn tự hủy ·"))).perform(click());onView(withText("5 phút")).perform(scrollTo(),click());onView(withText("Lưu")).perform(click());
            until(()->ConversationSettings.contact(id).optInt("disappearSeconds")==300);
            onView(withText("Tin nhắn tự hủy · 5 phút")).check(matches(isDisplayed()));
            scenario.onActivity(a->{try{snapshotSettings(a);}catch(Exception e){throw new AssertionError(e);}});
            onView(withText("Chọn tin nhắn để xóa")).perform(click());
            onView(withText("Bạn: Earlier chat 0")).perform(click());onView(withText("QA contact: Earlier chat 1")).perform(click());onView(withText("Xóa (2)")).perform(click());
            onView(withText("Xóa 2 tin nhắn?")).check(matches(isDisplayed()));onView(withId(android.R.id.button1)).perform(click());
            until(()->ConnectionService.messages.length()==28);assertTrue(ConversationSettings.contact(id).optLong("historyVersion")>0);
            openSettings();onView(withText("Xóa lịch sử hội thoại")).perform(click());onView(withId(android.R.id.button1)).perform(click());
            onView(withText("Nhập đúng XÓA để xác nhận.")).check(matches(isDisplayed()));assertEquals(28,ConnectionService.messages.length());
            onView(withContentDescription("Nhập XÓA để xác nhận")).perform(replaceText("XÓA"),closeSoftKeyboard());onView(withId(android.R.id.button1)).perform(click());until(()->ConnectionService.messages.length()==0);
            HistoryCache cache=new HistoryCache(context,pin);until(()->cache.messages(id).length()==0);
            openSettings();onView(withText("Đặt tên riêng")).perform(click());
            ConnectionService.current.submit(ConnectionService.packet("lock",id),error->{});
            until(()->ConversationSettings.contact(id).optBoolean("locked"));
            onView(withText(containsString("Hội thoại được bảo vệ"))).check(matches(isDisplayed()));
            scenario.onActivity(a->{try{CompanionTest.snapshotCurrent(a,"android-conversation-locked.png");}catch(Exception e){throw new AssertionError(e);}});
            context.stopService(new Intent(context,ConnectionService.class));until(()->ConnectionService.current==null);assertEquals(0,cache.messages(id).length());
        }finally{context.stopService(new Intent(context,ConnectionService.class));until(()->ConnectionService.current==null);DeviceKeys.forget(context);ConnectionService.selected="";ConnectionService.conversations=new JSONArray();ConnectionService.messages=new JSONArray();}
    }
    @Test public void invalidationErasesOnlyTheAffectedEncryptedCache()throws Exception{
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();DeviceKeys.ensure();HistoryCache.clearAll(context);HistoryCache cache=new HistoryCache(context,"A".repeat(64));
        cache.saveMessages("first",new JSONArray().put(new JSONObject().put("id","1").put("text","private")));
        cache.saveMessages("second",new JSONArray().put(new JSONObject().put("id","2").put("text","keep")));
        cache.clearConversation("first");assertEquals(0,cache.messages("first").length());assertEquals(1,cache.messages("second").length());HistoryCache.clearAll(context);
    }
    private static void snapshotSettings(MainActivity activity)throws Exception{
        var owner=MainActivity.class.getDeclaredField("conversationSettings");owner.setAccessible(true);
        var field=ConversationSettings.class.getDeclaredField("dialog");field.setAccessible(true);
        android.app.AlertDialog dialog=(android.app.AlertDialog)field.get(owner.get(activity));android.view.View view=dialog.getWindow().getDecorView();
        android.graphics.Bitmap bitmap=android.graphics.Bitmap.createBitmap(view.getWidth(),view.getHeight(),android.graphics.Bitmap.Config.ARGB_8888);view.draw(new android.graphics.Canvas(bitmap));
        java.io.File file=new java.io.File(activity.getExternalFilesDir(null),"android-conversation-settings.png");
        try(var out=new java.io.FileOutputStream(file)){bitmap.compress(android.graphics.Bitmap.CompressFormat.PNG,100,out);}bitmap.recycle();
        for(String command:new String[]{"mkdir -p /data/local/tmp/lantern-qa","cp "+file.getAbsolutePath()+" /data/local/tmp/lantern-qa/"+file.getName()})try(var in=new android.os.ParcelFileDescriptor.AutoCloseInputStream(InstrumentationRegistry.getInstrumentation().getUiAutomation().executeShellCommand(command))){while(in.read()!=-1){}}
    }
}
