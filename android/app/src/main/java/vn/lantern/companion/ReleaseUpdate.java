package vn.lantern.companion;

import org.json.*;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.*;
import java.util.function.IntConsumer;

/** Stable GitHub releases only. All persisted metadata is validated again before use. */
final class ReleaseUpdate {
    static final String API = "https://api.github.com/repos/zeusato/Lantern/releases/latest";
    static final String ASSET = "Lantern-Android.apk", SUMS = "ANDROID-SHA256SUMS.txt";
    static final long MAX_APK = 100L * 1024 * 1024;
    final String version, url, sumsUrl, digest;
    final long size;
    ReleaseUpdate(String version, String url, String sumsUrl, String digest, long size) {
        this.version=version; this.url=url; this.sumsUrl=sumsUrl; this.digest=digest; this.size=size;
    }
    static int code(String version) throws IOException {
        if (!version.matches("[0-9]{1,3}\\.[0-9]{1,2}\\.[0-9]{1,2}")) throw new IOException("Phiên bản cập nhật không hợp lệ.");
        String[] p=version.split("\\."); return Integer.parseInt(p[0])*10000+Integer.parseInt(p[1])*100+Integer.parseInt(p[2]);
    }
    static ReleaseUpdate parse(JSONObject json) throws Exception {
        if (json.optBoolean("draft",true)||json.optBoolean("prerelease",true)) throw new IOException("Chỉ nhận bản phát hành ổn định.");
        String tag=json.getString("tag_name"); if (!tag.startsWith("v")) throw new IOException("Thiếu mã phiên bản.");
        String version=tag.substring(1); code(version);
        JSONObject apk=null, sums=null; JSONArray assets=json.getJSONArray("assets");
        for(int i=0;i<assets.length();i++) { JSONObject a=assets.getJSONObject(i);
            if(ASSET.equals(a.optString("name"))) { if(apk!=null) throw new IOException("Trùng gói cập nhật."); apk=a; }
            if(SUMS.equals(a.optString("name"))) { if(sums!=null) throw new IOException("Trùng checksum."); sums=a; }
        }
        if(apk==null||sums==null) throw new IOException("Bản phát hành chưa có đủ APK và checksum.");
        String label=apk.optString("label","");
        if(label.startsWith("Android v")) {
            String apkVersion=label.substring("Android v".length());
            if(code(apkVersion)>code(version)) throw new IOException("Phiên bản APK vượt quá phiên bản phát hành.");
            version=apkVersion;
        }
        String base="https://github.com/zeusato/Lantern/releases/download/"+tag+"/";
        String url=apk.getString("browser_download_url"), sumUrl=sums.getString("browser_download_url");
        if(!url.equals(base+ASSET)||!sumUrl.equals(base+SUMS)) throw new IOException("Nguồn cập nhật không hợp lệ.");
        long size=apk.getLong("size"); if(size<=0||size>MAX_APK) throw new IOException("Kích thước APK không hợp lệ.");
        String digest=apk.isNull("digest")?"":apk.optString("digest","");
        if(!digest.isEmpty()&&!digest.matches("sha256:[a-fA-F0-9]{64}")) throw new IOException("Checksum GitHub không hợp lệ.");
        return new ReleaseUpdate(version,url,sumUrl,digest.toLowerCase(Locale.ROOT),size);
    }
    static boolean allowed(URL url) {
        return url.getProtocol().equals("https") && url.getUserInfo()==null && (url.getPort()==-1||url.getPort()==443)
            && Arrays.asList("api.github.com","github.com","release-assets.githubusercontent.com","objects.githubusercontent.com","github-releases.githubusercontent.com").contains(url.getHost().toLowerCase(Locale.ROOT));
    }
    static HttpURLConnection open(String address) throws Exception {
        URL url=new URL(address);
        for(int i=0;i<6;i++) {
            if(!allowed(url)) throw new IOException("Đường tải không được phép.");
            HttpURLConnection c=(HttpURLConnection)url.openConnection(); c.setInstanceFollowRedirects(false);
            c.setConnectTimeout(15000); c.setReadTimeout(20000); c.setRequestProperty("User-Agent","Lantern-Android-Updater");
            c.setRequestProperty("Accept","application/vnd.github+json"); c.setRequestProperty("Accept-Encoding","identity");
            int status=c.getResponseCode();
            if(status>=300&&status<400) { String next=c.getHeaderField("Location"); c.disconnect(); if(next==null) break; url=new URL(url,next); continue; }
            if(status!=200) { c.disconnect(); throw new IOException(status==403||status==429?"GitHub đang giới hạn lượt kiểm tra. Hãy thử lại sau.":"Không tải được cập nhật (HTTP "+status+")."); }
            return c;
        }
        throw new IOException("Quá nhiều lần chuyển hướng.");
    }
    static byte[] fetch(String address,int limit) throws Exception {
        HttpURLConnection c=open(address);
        try(InputStream in=c.getInputStream(); ByteArrayOutputStream out=new ByteArrayOutputStream()) {
            byte[] b=new byte[8192]; int n; while((n=in.read(b))!=-1) { if(out.size()+n>limit) throw new IOException("Dữ liệu cập nhật quá lớn."); out.write(b,0,n); } return out.toByteArray();
        } finally { c.disconnect(); }
    }
    static String checksum(String text,String file) throws IOException {
        String result=null;
        for(String line:text.split("\\r?\\n")) {
            String[] p=line.trim().split("\\s+");
            if(p.length==2&&p[1].replaceFirst("^\\*","").equals(file)) {
                if(result!=null||!p[0].matches("[a-fA-F0-9]{64}")) throw new IOException("Checksum không hợp lệ."); result=p[0].toLowerCase(Locale.ROOT);
            }
        }
        if(result==null) throw new IOException("Thiếu checksum cho APK."); return result;
    }
    static String sha256(File file) throws Exception {
        MessageDigest digest=MessageDigest.getInstance("SHA-256");
        try(InputStream in=new FileInputStream(file)) { byte[] b=new byte[65536]; int n; while((n=in.read(b))!=-1) digest.update(b,0,n); }
        StringBuilder hex=new StringBuilder(); for(byte b:digest.digest()) hex.append(String.format(Locale.ROOT,"%02x",b&255)); return hex.toString();
    }
    void verify(File file,String sha) throws Exception {
        if(!sha.matches("[a-f0-9]{64}")||file.length()!=size||!sha256(file).equals(sha)||(!digest.isEmpty()&&!digest.equals("sha256:"+sha))) throw new IOException("Gói tải về không khớp checksum. Hãy tải lại.");
    }
    void download(File target,String sha,IntConsumer progress) throws Exception {
        File part=new File(target.getParentFile(),"download.part"); HttpURLConnection c=open(url);
        try {
            long length=c.getContentLengthLong(); if(length>0&&length!=size) throw new IOException("Kích thước tải về không khớp.");
            try(InputStream in=c.getInputStream(); OutputStream out=new FileOutputStream(part)) {
                byte[] b=new byte[65536]; int n,previous=-1; long count=0;
                while((n=in.read(b))!=-1) { count+=n; if(count>size) throw new IOException("APK vượt kích thước đã công bố."); out.write(b,0,n); int p=(int)(100*count/size); if(p!=previous) { previous=p; progress.accept(p); } }
            }
            verify(part,sha);
            java.nio.file.Files.move(part.toPath(),target.toPath(),java.nio.file.StandardCopyOption.REPLACE_EXISTING);
        } finally { c.disconnect(); part.delete(); }
    }
}
