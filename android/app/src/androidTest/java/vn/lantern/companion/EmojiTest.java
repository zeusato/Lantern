package vn.lantern.companion;

import android.text.Spanned;
import android.text.style.ImageSpan;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.Test;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public class EmojiTest {
    @Test public void pcArtworkShortcodesAndCopyableSpans(){
        EmojiCatalog catalog=EmojiCatalog.get(InstrumentationRegistry.getInstrumentation().getTargetContext());
        assertTrue(catalog.entries.size()>1000);
        assertEquals("Chào 😃 ❤️",catalog.replace("Chào :D <3",true));
        assertEquals("x :)",catalog.replace("x :)",false));
        assertEquals("x 🙂 ",catalog.replace("x :) ",false));
        assertEquals("https://site/:heart: `:)`",catalog.replace("https://site/:heart: `:)`",true));
        assertEquals("```\n:)\n``` a:) C:\\folder\\:heart: 🙂",catalog.replace("```\n:)\n``` a:) C:\\folder\\:heart: :)",true));
        String plain="Tin nhắn thông thường không chứa emoji. ".repeat(100);
        assertEquals(plain,catalog.render(plain,16).toString());
        Spanned rendered=(Spanned)catalog.render("x :) ❤️👍",16);
        assertEquals("x :) ❤️👍",rendered.toString());
        assertEquals(3,rendered.getSpans(0,rendered.length(),ImageSpan.class).length);
        for(EmojiCatalog.Entry item:catalog.entries)assertNotNull(item.file(),catalog.image(item,32));
    }
}
