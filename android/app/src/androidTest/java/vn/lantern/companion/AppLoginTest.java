package vn.lantern.companion;

import android.content.Context;
import android.content.Intent;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.Test;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;
import static androidx.test.espresso.Espresso.onView;
import static androidx.test.espresso.assertion.ViewAssertions.matches;
import static androidx.test.espresso.matcher.ViewMatchers.*;

@RunWith(AndroidJUnit4.class)
public class AppLoginTest {
    @Test public void cachedPairingNeverOpensChatWithoutPassword()throws Exception{
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();context.stopService(new Intent(context,ConnectionService.class));
        AppSession.lock();DeviceKeys.save(context,new JSONObject().put("pc","A".repeat(64)).put("addresses",new JSONArray()).put("port",45854).put("name","Synthetic PC"));
        ConnectionService.conversations=new JSONArray().put(new JSONObject().put("id","b".repeat(64)).put("name","PRIVATE CACHED CONTACT"));
        try(ActivityScenario<MainActivity> ignored=ActivityScenario.launch(MainActivity.class)){
            onView(withText("Đăng nhập Lantern")).check(matches(isDisplayed()));
            onView(withContentDescription("Mật khẩu tài khoản Lantern")).check(matches(isDisplayed()));
            onView(withText("PRIVATE CACHED CONTACT")).check(androidx.test.espresso.assertion.ViewAssertions.doesNotExist());
            assertFalse(AppSession.unlocked());
        }finally{context.stopService(new Intent(context,ConnectionService.class));DeviceKeys.forget(context);AppSession.lock();}
    }
    @Test public void lockInvalidatesPendingAuthenticationAndDoesNotPersistSession(){
        AppSession.invalidate();long attempt=AppSession.generation();AppSession.invalidate();
        assertFalse(AppSession.authenticate(attempt));assertFalse(AppSession.unlocked());
        assertTrue(AppSession.authenticate(AppSession.generation()));assertTrue(AppSession.unlocked());
        AppSession.invalidate();assertFalse(AppSession.unlocked());
    }
}
