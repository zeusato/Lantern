package vn.lantern.companion;

import android.content.Context;
import android.graphics.*;
import android.os.SystemClock;
import android.view.*;
import java.io.File;
import java.util.concurrent.Executors;

/** Transparent sticker canvas. Decode off the UI thread and animate only in the viewport. */
@SuppressWarnings("deprecation")
final class StickerView extends View {
    private static final java.util.concurrent.ExecutorService decoder=Executors.newFixedThreadPool(2);
    private final File file;
    private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG|Paint.FILTER_BITMAP_FLAG);
    private final Rect visible=new Rect();
    private Bitmap bitmap;
    private Movie movie;
    private long started;
    private int generation;
    private boolean failed;
    private final Runnable frame=()->{if(active())invalidate();};
    private final ViewTreeObserver.OnScrollChangedListener scrolled=()->{removeCallbacks(frame);if(active())invalidate();};
    StickerView(Context context,File file,String name){super(context);this.file=file;setContentDescription("Sticker "+name);int size=(int)(160*getResources().getDisplayMetrics().density);setLayoutParams(new android.widget.LinearLayout.LayoutParams(size,size));}
    private boolean active(){return isAttachedToWindow()&&getWindowVisibility()==VISIBLE&&isShown()&&getGlobalVisibleRect(visible);}
    @Override protected void onAttachedToWindow(){super.onAttachedToWindow();getViewTreeObserver().addOnScrollChangedListener(scrolled);int ticket=++generation;decoder.execute(()->{
        Bitmap picture=null;Movie animation=null;
        try{if(file.length()>8*1024*1024)throw new java.io.IOException();BitmapFactory.Options bounds=new BitmapFactory.Options();bounds.inJustDecodeBounds=true;BitmapFactory.decodeFile(file.getPath(),bounds);if(bounds.outWidth<1||bounds.outHeight<1||bounds.outWidth>2048||bounds.outHeight>2048)throw new java.io.IOException();
            animation=Movie.decodeFile(file.getPath());if(animation==null||animation.duration()==0){animation=null;BitmapFactory.Options options=new BitmapFactory.Options();options.inSampleSize=Math.max(1,Math.max(bounds.outWidth,bounds.outHeight)/480);picture=BitmapFactory.decodeFile(file.getPath(),options);}
        }catch(Exception ignored){}
        Bitmap result=picture;Movie animated=animation;post(()->{if(ticket!=generation||!isAttachedToWindow())return;bitmap=result;movie=animated;failed=bitmap==null&&movie==null;started=SystemClock.uptimeMillis();invalidate();});
    });}
    @Override protected void onDetachedFromWindow(){generation++;removeCallbacks(frame);if(getViewTreeObserver().isAlive())getViewTreeObserver().removeOnScrollChangedListener(scrolled);bitmap=null;movie=null;super.onDetachedFromWindow();}
    @Override public void onVisibilityAggregated(boolean visible){super.onVisibilityAggregated(visible);removeCallbacks(frame);if(visible)invalidate();}
    @Override protected void onDraw(Canvas canvas){super.onDraw(canvas);int w=movie!=null?movie.width():bitmap!=null?bitmap.getWidth():0,h=movie!=null?movie.height():bitmap!=null?bitmap.getHeight():0;
        if(w>0&&h>0){float scale=Math.min((float)getWidth()/w,(float)getHeight()/h);canvas.save();canvas.translate((getWidth()-w*scale)/2,(getHeight()-h*scale)/2);canvas.scale(scale,scale);if(movie!=null){movie.setTime((int)((SystemClock.uptimeMillis()-started)%Math.max(1,movie.duration())));movie.draw(canvas,0,0,paint);}else canvas.drawBitmap(bitmap,0,0,paint);canvas.restore();if(movie!=null&&active()){removeCallbacks(frame);postDelayed(frame,40);}}
        else {paint.setColor(0xff8b9e98);paint.setTextSize(12*getResources().getDisplayMetrics().scaledDensity);canvas.drawText(failed?"Không đọc được sticker":"Đang tải sticker…",4,getHeight()/2f,paint);}
    }
}
