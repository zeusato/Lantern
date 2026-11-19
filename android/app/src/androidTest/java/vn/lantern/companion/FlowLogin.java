package vn.lantern.companion;

import android.content.Context;
import android.content.Intent;
import androidx.test.platform.app.InstrumentationRegistry;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;
import static org.junit.Assert.*;

/** Authenticates only against the synthetic TLS fixture, before launching a flow scenario. */
final class FlowLogin {
    static void authenticate(Context context)throws Exception{
        context.startForegroundService(new Intent(context,ConnectionService.class));
        long deadline=System.currentTimeMillis()+25000;
        while(ConnectionService.current==null||!ConnectionService.connected){
            if(System.currentTimeMillis()>deadline)throw new AssertionError("Fixture did not connect");
            Thread.sleep(50);
        }
        CountDownLatch done=new CountDownLatch(1);AtomicReference<String> error=new AtomicReference<>();
        InstrumentationRegistry.getInstrumentation().runOnMainSync(()->ConnectionService.current.loginAccount("qa-account-password",message->{error.set(message);done.countDown();}));
        assertTrue("Account login timed out",done.await(25,TimeUnit.SECONDS));
        assertNull(error.get());assertTrue(AppSession.unlocked());
    }
}
