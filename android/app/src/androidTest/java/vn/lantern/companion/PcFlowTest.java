package vn.lantern.companion;

import android.content.*;
import android.view.*;
import android.widget.*;
import androidx.test.core.app.ActivityScenario;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.json.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.function.BooleanSupplier;
import static org.junit.Assert.*;
import static androidx.test.espresso.Espresso.*;
import static androidx.test.espresso.action.ViewActions.*;
import static androidx.test.espresso.assertion.ViewAssertions.*;
import static androidx.test.espresso.matcher.ViewMatchers.*;

@RunWith(AndroidJUnit4.class)
public class PcFlowTest {
    private static void until(BooleanSupplier condition)throws Exception{long end=System.currentTimeMillis()+20000;while(!condition.getAsBoolean()){if(System.currentTimeMillis()>end)throw new AssertionError("PC status did not reach Android");Thread.sleep(50);}}
    private static boolean contains(View view,String text){if(view instanceof TextView t&&t.getText().toString().contains(text))return true;if(view instanceof ViewGroup group)for(int i=0;i<group.getChildCount();i++)if(contains(group.getChildAt(i),text))return true;return false;}
    private static void waitText(ActivityScenario<PcActivity> scenario,String value)throws Exception{until(()->{AtomicBoolean found=new AtomicBoolean();scenario.onActivity(a->found.set(contains(a.getWindow().getDecorView(),value)));return found.get();});}
    private static androidx.test.espresso.ViewAction inspectClick(){return new androidx.test.espresso.ViewAction(){public org.hamcrest.Matcher<View> getConstraints(){return isAssignableFrom(Button.class);}public String getDescription(){return "Start inspection and prevent duplicate requests";}public void perform(androidx.test.espresso.UiController ui,View view){view.performClick();assertFalse(view.isEnabled());view.performClick();}};}
    @Test public void aiStatusReturnsOverTlsAndRecoversFromTimeoutErrorAndBackground()throws Exception{
        String pin=InstrumentationRegistry.getArguments().getString("pcPin");org.junit.Assume.assumeNotNull(pin);
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();
        DeviceKeys.ensure();DeviceKeys.save(context,new JSONObject().put("pc",pin).put("name","QA PC").put("port",45854).put("addresses",new JSONArray().put("10.0.2.2")).put("nonce","q".repeat(43)));
        ConnectionService.selected="";
        FlowLogin.authenticate(context);
        try(ActivityScenario<MainActivity> main=ActivityScenario.launch(MainActivity.class)){
            until(()->ConnectionService.connected&&ConnectionService.current.controlConnection()!=null);
            try(ActivityScenario<PcActivity> pc=ActivityScenario.launch(PcActivity.class)){
                waitText(pc,"AI · Hiện cửa sổ và kiểm tra");
                onView(withText("AI · Hiện cửa sổ và kiểm tra")).perform(inspectClick());
                waitText(pc,"Claude · Đang làm việc");
                onView(withText("ChatGPT / Codex · Sẵn sàng")).check(matches(isDisplayed()));
                onView(withContentDescription("Đang xử lý yêu cầu PC")).check(matches(withEffectiveVisibility(Visibility.GONE)));
                onView(withText("Kiểm tra lại")).perform(inspectClick());
                waitText(pc,"QA provider timeout");
                onView(withText("Claude · Chưa xác định")).check(matches(isDisplayed()));
                onView(withText("ChatGPT / Codex · Sẵn sàng")).check(matches(isDisplayed()));
                onView(withText("Kiểm tra lại")).perform(inspectClick());
                waitText(pc,"QA inspection error");
                onView(withText("Kiểm tra lại")).check(matches(isEnabled())).perform(inspectClick());
                pc.moveToState(androidx.lifecycle.Lifecycle.State.STARTED);Thread.sleep(1700);pc.moveToState(androidx.lifecycle.Lifecycle.State.RESUMED);
                waitText(pc,"Thao tác đã dừng khi rời màn hình");
                onView(withText("Kiểm tra lại")).check(matches(isEnabled())).perform(inspectClick());
                waitText(pc,"Claude · Đang làm việc");
                pc.onActivity(a->{try{CompanionTest.snapshotCurrent(a,"android-pc-ai-status.png");}catch(Exception e){throw new AssertionError(e);}});
                pc.moveToState(androidx.lifecycle.Lifecycle.State.CREATED);
                assertTrue("Switching apps preserves account access",AppSession.unlocked());
                pc.moveToState(androidx.lifecycle.Lifecycle.State.RESUMED);
                assertTrue(AppSession.unlocked());
                pc.moveToState(androidx.lifecycle.Lifecycle.State.CREATED);
                AppSession.lock();assertFalse(AppSession.unlocked());
                assertNull(ConnectionService.current.controlConnection());
                pc.moveToState(androidx.lifecycle.Lifecycle.State.RESUMED);
                onView(withText("Đăng nhập Lantern")).check(matches(isDisplayed()));
            }
        }finally{context.stopService(new Intent(context,ConnectionService.class));until(()->ConnectionService.current==null);DeviceKeys.forget(context);ConnectionService.selected="";ConnectionService.conversations=new JSONArray();ConnectionService.messages=new JSONArray();}
    }
}
