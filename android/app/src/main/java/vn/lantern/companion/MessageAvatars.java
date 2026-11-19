package vn.lantern.companion;

import android.content.Context;
import android.graphics.*;
import android.graphics.drawable.GradientDrawable;
import android.util.Base64;
import android.util.LruCache;
import android.view.*;
import android.widget.*;

final class MessageAvatars {
    private final LruCache<String,Bitmap> pictures=new LruCache<>(48);
    View create(Context context,String name,String data){
        float density=context.getResources().getDisplayMetrics().density;int size=(int)(32*density);
        Bitmap picture=pictures.get(data);
        if(picture==null&&!data.isEmpty()&&data.length()<=218456)try{
            byte[] bytes=Base64.decode(data,Base64.DEFAULT);BitmapFactory.Options bounds=new BitmapFactory.Options();bounds.inJustDecodeBounds=true;BitmapFactory.decodeByteArray(bytes,0,bytes.length,bounds);
            if(bounds.outWidth==192&&bounds.outHeight==192){BitmapFactory.Options options=new BitmapFactory.Options();options.inSampleSize=2;picture=BitmapFactory.decodeByteArray(bytes,0,bytes.length,options);if(picture!=null)pictures.put(data,picture);}
        }catch(IllegalArgumentException ignored){}
        View avatar;
        if(picture!=null){ImageView image=new ImageView(context);image.setImageBitmap(picture);image.setScaleType(ImageView.ScaleType.CENTER_CROP);avatar=image;}
        else {TextView initials=new TextView(context);String label=name.trim();initials.setText(label.isEmpty()?"?":label.substring(0,label.offsetByCodePoints(0,1)).toUpperCase(java.util.Locale.getDefault()));initials.setTextSize(14);initials.setTextColor(Color.rgb(234,243,246));initials.setGravity(Gravity.CENTER);avatar=initials;}
        GradientDrawable background=new GradientDrawable();background.setColor(Color.rgb(28,92,88));background.setShape(GradientDrawable.OVAL);avatar.setBackground(background);avatar.setClipToOutline(true);avatar.setContentDescription("Ảnh đại diện "+name);avatar.setLayoutParams(new android.widget.FrameLayout.LayoutParams(size,size));
        FrameLayout slot=new FrameLayout(context);slot.addView(avatar);slot.setLayoutParams(new LinearLayout.LayoutParams((int)(40*density),size));return slot;
    }
}
