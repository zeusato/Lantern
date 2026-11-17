package vn.lantern.companion;

import org.json.JSONObject;
import java.time.*;

final class MessageGrouping {
    static boolean joins(JSONObject previous,JSONObject next) {
        if(previous==null||next==null||previous.optBoolean("outgoing")!=next.optBoolean("outgoing")||!previous.optString("conversationId").equals(next.optString("conversationId")))return false;
        if(!previous.optBoolean("outgoing")&&(previous.optString("senderId").isEmpty()||!previous.optString("senderId").equals(next.optString("senderId"))))return false;
        try {
            OffsetDateTime a=OffsetDateTime.parse(previous.getString("time")),b=OffsetDateTime.parse(next.getString("time"));
            long gap=Duration.between(a,b).toMillis();ZoneId zone=ZoneId.systemDefault();
            return gap>=0&&gap<=300000&&a.atZoneSameInstant(zone).toLocalDate().equals(b.atZoneSameInstant(zone).toLocalDate());
        }catch(Exception ignored){return false;}
    }
}
