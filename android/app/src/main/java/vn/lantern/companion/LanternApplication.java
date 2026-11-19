package vn.lantern.companion;

import android.app.Activity;
import android.app.Application;
import android.os.Bundle;

public final class LanternApplication extends Application {
    private int started;
    @Override public void onCreate(){super.onCreate();registerActivityLifecycleCallbacks(new ActivityLifecycleCallbacks(){
        public void onActivityCreated(Activity a,Bundle state){}
        public void onActivityStarted(Activity a){started++;}
        public void onActivityResumed(Activity a){}
        public void onActivityPaused(Activity a){}
        public void onActivityStopped(Activity a){started=Math.max(0,started-1);if(started==0&&a.isFinishing()&&a.isTaskRoot()&&!a.isChangingConfigurations())AppSession.lock();}
        public void onActivitySaveInstanceState(Activity a,Bundle state){}
        public void onActivityDestroyed(Activity a){}
    });}
}
