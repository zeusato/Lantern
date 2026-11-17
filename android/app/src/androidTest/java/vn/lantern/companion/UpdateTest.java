package vn.lantern.companion;

import android.content.Context;
import android.content.pm.Signature;
import androidx.test.core.app.ActivityScenario;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.json.*;
import java.io.*;
import java.net.URL;
import java.nio.file.Files;
import static org.junit.Assert.*;
import static androidx.test.espresso.Espresso.onView;
import static androidx.test.espresso.matcher.ViewMatchers.*;
import static androidx.test.espresso.assertion.ViewAssertions.matches;

@RunWith(AndroidJUnit4.class)
public class UpdateTest {
    @Test public void groupingRespectsAuthorTimeDirectionAndDay() throws Exception {
        java.time.OffsetDateTime start=java.time.ZonedDateTime.now().withHour(10).withMinute(0).withSecond(0).withNano(0).toOffsetDateTime();
        JSONObject a=new JSONObject().put("senderId","a").put("conversationId","chat").put("time",start.toString()).put("outgoing",false);
        JSONObject b=new JSONObject(a.toString()).put("time",start.plusSeconds(300).toString());assertTrue(MessageGrouping.joins(a,b));
        b.put("time",start.plusSeconds(301).toString());assertFalse(MessageGrouping.joins(a,b));
        b.put("time",start.minusSeconds(1).toString());assertFalse(MessageGrouping.joins(a,b));
        b.put("time",start.plusSeconds(1).toString()).put("senderId","other");assertFalse(MessageGrouping.joins(a,b));
        b.put("senderId","a").put("outgoing",true);assertFalse(MessageGrouping.joins(a,b));
        b.put("outgoing",false).put("conversationId","other");assertFalse(MessageGrouping.joins(a,b));
        a.put("time",start.withHour(23).withMinute(59).toString());b.put("conversationId","chat").put("time",start.plusDays(1).withHour(0).toString());assertFalse(MessageGrouping.joins(a,b));
        a.put("outgoing",true).remove("senderId");b.put("outgoing",true).put("time",a.getString("time"));assertTrue(MessageGrouping.joins(a,b));
    }
    private JSONObject metadata() throws Exception {
        String base="https://github.com/zeusato/Lantern/releases/download/v9.9.9/";
        return new JSONObject().put("draft",false).put("prerelease",false).put("tag_name","v9.9.9").put("assets",new JSONArray()
            .put(new JSONObject().put("name",ReleaseUpdate.ASSET).put("browser_download_url",base+ReleaseUpdate.ASSET).put("size",3))
            .put(new JSONObject().put("name",ReleaseUpdate.SUMS).put("browser_download_url",base+ReleaseUpdate.SUMS)));
    }
    @Test public void stableMetadataRejectsDowngradeParsingAbuseAndForeignSources() throws Exception {
        assertTrue(ReleaseUpdate.code("1.10.0")>ReleaseUpdate.code("1.9.99"));
        assertThrows(Exception.class,()->ReleaseUpdate.code("1.2.3-beta"));
        assertThrows(Exception.class,()->ReleaseUpdate.code("1.200.3"));
        assertEquals("9.9.9",ReleaseUpdate.parse(metadata()).version);
        assertThrows(Exception.class,()->ReleaseUpdate.parse(metadata().put("prerelease",true)));
        JSONObject foreign=metadata();foreign.getJSONArray("assets").getJSONObject(0).put("browser_download_url","https://github.com/evil/Lantern/releases/download/v9.9.9/Lantern-Android.apk");
        assertThrows(Exception.class,()->ReleaseUpdate.parse(foreign));
        JSONObject incomplete=metadata();incomplete.getJSONArray("assets").remove(1);assertThrows(Exception.class,()->ReleaseUpdate.parse(incomplete));
        assertTrue(ReleaseUpdate.allowed(new URL("https://release-assets.githubusercontent.com/a")));
        assertFalse(ReleaseUpdate.allowed(new URL("https://github.com.evil.test/a")));
        assertFalse(ReleaseUpdate.allowed(new URL("http://github.com/a")));
        assertFalse(ReleaseUpdate.allowed(new URL("https://u:p@github.com/a")));
        assertFalse(ReleaseUpdate.allowed(new URL("https://github.com:444/a")));
    }
    @Test public void corruptedAndPartialDownloadsNeverPassVerification() throws Exception {
        File file=File.createTempFile("update-test",".apk",InstrumentationRegistry.getInstrumentation().getTargetContext().getCacheDir());
        try {
            Files.write(file.toPath(),new byte[]{1,2,3});String sha=ReleaseUpdate.sha256(file);
            assertEquals(sha,ReleaseUpdate.checksum(sha+"  "+ReleaseUpdate.ASSET,ReleaseUpdate.ASSET));
            assertThrows(IOException.class,()->ReleaseUpdate.checksum(sha+"  "+ReleaseUpdate.ASSET+"\n"+sha+"  "+ReleaseUpdate.ASSET,ReleaseUpdate.ASSET));
            ReleaseUpdate release=ReleaseUpdate.parse(metadata());release.verify(file,sha);
            Files.write(file.toPath(),new byte[]{1,9,3});assertThrows(Exception.class,()->release.verify(file,sha));
            Files.write(file.toPath(),new byte[]{1,2});assertThrows(Exception.class,()->release.verify(file,sha));
        } finally {file.delete();}
    }
    @Test public void realApkValidationAcceptsSameKeyNewerVersionOnly() throws Exception {
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();
        File candidate=File.createTempFile("candidate",".apk",context.getCacheDir());
        try(InputStream in=InstrumentationRegistry.getInstrumentation().getContext().getAssets().open("candidate.apk");OutputStream out=new FileOutputStream(candidate)){byte[] b=new byte[65536];int n;while((n=in.read(b))!=-1)out.write(b,0,n);}
        try {
            MobileUpdater.verifyApk(context,candidate,"9.9.9");
            assertThrows(Exception.class,()->MobileUpdater.verifyApk(context,candidate,"9.9.8"));
            File installed=new File(context.getApplicationInfo().sourceDir);
            assertThrows(Exception.class,()->MobileUpdater.verifyApk(context,installed,context.getPackageManager().getPackageInfo(context.getPackageName(),0).versionName));
            File testApk=new File(InstrumentationRegistry.getInstrumentation().getContext().getApplicationInfo().sourceDir);
            assertThrows(Exception.class,()->MobileUpdater.verifyApk(context,testApk,"9.9.9"));
            assertFalse(MobileUpdater.sameSigners(new Signature[]{new Signature("aa")},new Signature[]{new Signature("bb")}));
            assertFalse(MobileUpdater.sameSigners(null,null));
            assertFalse(MobileUpdater.sameSigners(new Signature[0],new Signature[0]));
        } finally {candidate.delete();}
    }
    @Test public void updateScreenWorksWithoutPcAndPreservesPreferences() throws Exception {
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();
        context.getSharedPreferences("updates",0).edit().putBoolean("autoCheck",false).putBoolean("autoDownload",false).putLong("lastAttempt",System.currentTimeMillis()).commit();
        try(ActivityScenario<UpdateActivity> scenario=ActivityScenario.launch(UpdateActivity.class)) {
            onView(withText("Cập nhật ứng dụng")).check(matches(isDisplayed()));
            onView(withText("Kiểm tra ngay")).check(matches(isDisplayed()));
            onView(withText("Cài đặt bản đã tải")).check(matches(org.hamcrest.Matchers.not(isEnabled())));
            scenario.recreate();onView(withText("Tự tải bản cập nhật")).check(matches(isNotChecked()));
        }
    }
}
