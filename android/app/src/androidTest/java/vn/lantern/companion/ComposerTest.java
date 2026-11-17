package vn.lantern.companion;

import android.content.Context;
import android.text.Editable;
import android.view.View;
import android.view.inputmethod.BaseInputConnection;
import android.view.inputmethod.InputMethodManager;
import android.widget.*;
import androidx.core.view.ViewCompat;
import androidx.core.view.WindowInsetsCompat;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.Test;
import org.junit.runner.RunWith;
import java.lang.reflect.*;
import java.time.OffsetDateTime;
import java.util.concurrent.atomic.AtomicReference;
import static org.junit.Assert.*;
import static androidx.test.espresso.Espresso.onView;
import static androidx.test.espresso.action.ViewActions.*;
import static androidx.test.espresso.assertion.ViewAssertions.matches;
import static androidx.test.espresso.matcher.ViewMatchers.*;

@RunWith(AndroidJUnit4.class)
public class ComposerTest {
    private static <T>T field(MainActivity a,String name)throws Exception{Field f=MainActivity.class.getDeclaredField(name);f.setAccessible(true);return (T)f.get(a);}
    private static void call(MainActivity a,String name)throws Exception{Method m=MainActivity.class.getDeclaredMethod(name);m.setAccessible(true);m.invoke(a);}
    private static JSONObject message(String id,String text)throws Exception{return new JSONObject().put("id",id).put("kind","text").put("text",text).put("senderId","peer").put("senderName","QA").put("time",OffsetDateTime.now().toString()).put("status","read");}
    @Test public void typingSurvivesStatusPushExpiryAndRecreation()throws Exception{
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();DeviceKeys.forget(context);ConnectionService.selected="";
        AtomicReference<Editable> buffer=new AtomicReference<>();AtomicReference<View> retainedRow=new AtomicReference<>();
        try(ActivityScenario<MainActivity> scenario=ActivityScenario.launch(MainActivity.class)){
            scenario.onActivity(a->{try{
                ConnectionService.selected="c".repeat(64);ConnectionService.connected=true;
                ConnectionService.conversations=new JSONArray().put(new JSONObject().put("id",ConnectionService.selected).put("name","QA").put("locked",false));
                ConnectionService.messages=new JSONArray().put(message("stable","Keep this row")).put(message("anchor","Keep this group boundary"));
                call(a,"build");
            }catch(Exception e){throw new AssertionError(e);}});
            onView(withContentDescription("Soạn tin nhắn")).perform(typeText("Dang go tieng Viet"));
            scenario.onActivity(a->{try{
                EditText editor=field(a,"composer");editor.setSelection(5);buffer.set(editor.getText());BaseInputConnection.setComposingSpans(editor.getText());
                // Start expiry after IME startup/typeText, which can be slow on CI emulators.
                ConnectionService.messages.put(message("expire","Short-lived").put("expiresAt",OffsetDateTime.now().plusSeconds(2).toString()));call(a,"refresh");
                retainedRow.set(((LinearLayout)field(a,"messagesBox")).getChildAt(0));
            }catch(Exception e){throw new AssertionError(e);}});
            for(int n=0;n<6;n++){
                final int index=n;scenario.onActivity(a->{try{
                    EditText editor=field(a,"composer");
                    // The IME may update its composing range between UI turns. A chat render
                    // must preserve the current range, not force an obsolete range back onto it.
                    int compositionStart=BaseInputConnection.getComposingSpanStart(editor.getText()),compositionEnd=BaseInputConnection.getComposingSpanEnd(editor.getText());
                    ConnectionService.status="Đang đồng bộ "+index;
                    JSONArray next=new JSONArray(ConnectionService.messages.toString());next.put(message("incoming-"+index,"Incoming "+index));ConnectionService.messages=next;call(a,"refresh");
                    assertSame(buffer.get(),editor.getText());assertTrue(editor.hasFocus());assertEquals(5,editor.getSelectionStart());assertEquals(compositionStart,BaseInputConnection.getComposingSpanStart(editor.getText()));assertEquals(compositionEnd,BaseInputConnection.getComposingSpanEnd(editor.getText()));
                    assertSame(retainedRow.get(),((LinearLayout)field(a,"messagesBox")).getChildAt(0));
                }catch(Exception e){throw new AssertionError(e);}});Thread.sleep(450);
            }
            scenario.onActivity(a->{try{
                EditText editor=field(a,"composer");assertTrue(((InputMethodManager)a.getSystemService(Context.INPUT_METHOD_SERVICE)).isActive(editor));
                assertTrue(ViewCompat.getRootWindowInsets(editor).isVisible(WindowInsetsCompat.Type.ime()));
                assertNull(((LinearLayout)field(a,"messagesBox")).findViewWithTag("expire"));
                BaseInputConnection.removeComposingSpans(editor.getText());
            }catch(Exception e){throw new AssertionError(e);}});
            onView(withContentDescription("Soạn tin nhắn")).perform(replaceText("Dong 1\nDong 2\nDong 3\nDong 4\nDong 5\nDong 6\nDong 7"));
            onView(withContentDescription("Gửi tin nhắn")).check(matches(isCompletelyDisplayed()));
            scenario.onActivity(a->{try{
                EditText editor=field(a,"composer");ImageButton send=field(a,"sendButton");assertNotNull(send.getDrawable());assertTrue(editor.getHeight()>send.getHeight());assertTrue(editor.getHeight()<=editor.getLineHeight()*6+editor.getPaddingTop()+editor.getPaddingBottom()+2);
                assertEquals(android.view.Gravity.TOP,editor.getGravity()&android.view.Gravity.VERTICAL_GRAVITY_MASK);
                CompanionTest.snapshotCurrent(a,"android-composer-stable.png");
            }catch(Exception e){throw new AssertionError(e);}});
            scenario.recreate();
            onView(withContentDescription("Soạn tin nhắn")).check(matches(withText("Dong 1\nDong 2\nDong 3\nDong 4\nDong 5\nDong 6\nDong 7"))).check(matches(hasFocus()));
            scenario.onActivity(a->{try{ConnectionService.conversations.getJSONObject(0).put("locked",true);call(a,"refresh");assertEquals(View.GONE,((View)field(a,"composerBar")).getVisibility());}catch(Exception e){throw new AssertionError(e);}});
        }finally{ConnectionService.selected="";ConnectionService.connected=false;ConnectionService.conversations=new JSONArray();ConnectionService.messages=new JSONArray();}
    }
}
