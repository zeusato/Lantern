package vn.lantern.companion;

import android.content.Context;
import android.view.*;
import android.widget.*;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.Test;
import org.junit.runner.RunWith;
import java.lang.reflect.*;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public class ChatGamesTest {
    private static final String PEER="c".repeat(64),ME="a".repeat(64),ID="1".repeat(32);
    private static <T>T field(Object a,String name)throws Exception{Field f=a.getClass().getDeclaredField(name);f.setAccessible(true);return (T)f.get(a);}
    private static void call(MainActivity a,String name)throws Exception{Method m=MainActivity.class.getDeclaredMethod(name);m.setAccessible(true);m.invoke(a);}
    private static JSONObject game(String status,String kind)throws Exception{return new JSONObject().put("me",ME).put("peerName","Minh Anh").put("connected",true).put("pending",false).put("ownBoard",new JSONArray()).put("state",new JSONObject().put("id",ID).put("host",ME).put("guest",PEER).put("starter",ME).put("turn",ME).put("kind",kind).put("status",status).put("revision",6).put("words",new JSONArray().put("học sinh").put("sinh nhật").put("nhật ký").put("ký ức")).put("board",new JSONArray()).put("fleets",new JSONObject()).put("shots",new JSONObject()).put("pendingCell",-1));}
    private static <T extends View>T find(View view,Class<T> type,String description){if(type.isInstance(view)&&(description==null||description.contentEquals(view.getContentDescription()==null?"":view.getContentDescription())))return type.cast(view);if(view instanceof ViewGroup parent)for(int i=0;i<parent.getChildCount();i++){T result=find(parent.getChildAt(i),type,description);if(result!=null)return result;}return null;}
    @Test public void splitChatGameDraftRotationAndPrivacy()throws Exception{
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();DeviceKeys.forget(context);AppSession.authenticate(AppSession.generation());ConnectionService.selected="";
        try(ActivityScenario<MainActivity> scenario=ActivityScenario.launch(MainActivity.class)){
            scenario.onActivity(a->{try{
                ConnectionService.selected=PEER;ConnectionService.connected=true;ConnectionService.conversations=new JSONArray().put(new JSONObject().put("id",PEER).put("name","Minh Anh").put("locked",false));
                ConnectionService.messages=new JSONArray();ConnectionService.games.put(PEER,game("invited","words"));call(a,"build");
                ChatGamesView games=field(a,"gamesView");assertEquals(View.GONE,games.getVisibility());
                ConnectionService.games.put(PEER,game("playing","words"));call(a,"refresh");assertEquals(View.VISIBLE,games.getVisibility());
                EditText composer=field(a,"composer");composer.setText("Vừa chơi vừa chat");composer.requestFocus();
                EditText input=find(games,EditText.class,"Nhập từ nối");assertNotNull(input);assertTrue(input.isEnabled());input.setText("chế");
            }catch(Exception e){throw new AssertionError(e);}});
            InstrumentationRegistry.getInstrumentation().waitForIdleSync();
            scenario.onActivity(a->{try{
                ChatGamesView games=field(a,"gamesView");LinearLayout parent=(LinearLayout)games.getParent();assertEquals(parent.getChildAt(0).getWidth(),games.getWidth(),1);assertTrue(((EditText)field(a,"composer")).hasFocus());CompanionTest.snapshotCurrent(a,"android-word-chain.png");
            }catch(Exception e){throw new AssertionError(e);}});
            scenario.recreate();
            scenario.onActivity(a->{try{
                ChatGamesView games=field(a,"gamesView");assertEquals("chế",find(games,EditText.class,"Nhập từ nối").getText().toString());assertEquals("Vừa chơi vừa chat",((EditText)field(a,"composer")).getText().toString());
                ConnectionService.conversations.getJSONObject(0).put("locked",true);call(a,"refresh");assertEquals(View.GONE,games.getVisibility());games.open();assertEquals(View.GONE,games.getVisibility());
                ConnectionService.conversations.getJSONObject(0).put("locked",false);ConnectionService.games.put(PEER,game("placing","ships"));call(a,"refresh");assertNotNull(find(games,SeekBar.class,"Phóng to bàn chơi"));CompanionTest.snapshotCurrent(a,"android-battleship.png");
                AppSession.invalidate();games.open();assertEquals(View.GONE,games.getVisibility());
            }catch(Exception e){throw new AssertionError(e);}});
        }finally{ConnectionService.selected="";ConnectionService.connected=false;ConnectionService.conversations=new JSONArray();ConnectionService.messages=new JSONArray();ConnectionService.games.clear();AppSession.invalidate();}
    }
}
