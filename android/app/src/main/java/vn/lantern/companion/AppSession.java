package vn.lantern.companion;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;

/** In-memory only. Pairing and cached history never grant an unlocked UI session. */
final class AppSession {
    private static boolean unlocked;
    private static long generation;
    static synchronized boolean unlocked(){return unlocked;}
    static synchronized long generation(){return generation;}
    static synchronized boolean authenticate(long expected){if(expected!=generation)return false;unlocked=true;return true;}
    static synchronized void invalidate(){unlocked=false;generation++;}
    static boolean paired(Context context){return context.getSharedPreferences("private",0).contains("connection");}
    static void lock(){invalidate();ConnectionService service=ConnectionService.current;if(service!=null)service.lockAccount();}
    static boolean guard(Activity activity){
        if(activity.isFinishing())return false;
        if(!paired(activity)||unlocked())return true;
        activity.startActivity(new Intent(activity,LoginActivity.class).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP|Intent.FLAG_ACTIVITY_SINGLE_TOP));activity.finish();return false;
    }
}
