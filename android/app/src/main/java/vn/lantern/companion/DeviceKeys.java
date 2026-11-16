package vn.lantern.companion;

import android.content.Context;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import org.json.JSONObject;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.spec.GCMParameterSpec;
import javax.net.ssl.*;
import javax.security.auth.x500.X500Principal;
import java.net.Socket;
import java.math.BigInteger;
import java.security.*;
import java.security.cert.X509Certificate;
import java.security.spec.ECGenParameterSpec;
import java.util.*;

final class DeviceKeys {
    private static final String ID = "lantern-device-v1", SECRET = "lantern-config-v1";
    static KeyStore store() throws Exception { KeyStore ks=KeyStore.getInstance("AndroidKeyStore"); ks.load(null); return ks; }
    static void ensure() throws Exception {
        KeyStore ks=store();
        if(!ks.containsAlias(ID)) {
            KeyPairGenerator generator=KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_EC,"AndroidKeyStore");
            generator.initialize(new KeyGenParameterSpec.Builder(ID,KeyProperties.PURPOSE_SIGN|KeyProperties.PURPOSE_VERIFY)
                // Conscrypt hashes TLS handshakes itself, then asks Keystore for a raw ECDSA signature.
                .setAlgorithmParameterSpec(new ECGenParameterSpec("secp256r1")).setDigests(KeyProperties.DIGEST_NONE,KeyProperties.DIGEST_SHA256,KeyProperties.DIGEST_SHA384,KeyProperties.DIGEST_SHA512)
                .setCertificateSubject(new X500Principal("CN=Lantern Android")).setCertificateSerialNumber(BigInteger.ONE)
                .setCertificateNotBefore(new Date(0)).setCertificateNotAfter(new Date(4102444800000L)).build()); generator.generateKeyPair();
        }
        if(!ks.containsAlias(SECRET)) {
            KeyGenerator generator=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");
            generator.init(new KeyGenParameterSpec.Builder(SECRET,KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build()); generator.generateKey();
        }
    }
    static String hex(byte[] bytes) { StringBuilder b=new StringBuilder(); for(byte v:bytes)b.append(String.format(Locale.ROOT,"%02X",v)); return b.toString(); }
    static String fingerprint(X509Certificate cert) throws Exception { return hex(MessageDigest.getInstance("SHA-256").digest(cert.getPublicKey().getEncoded())); }
    static String id() throws Exception { ensure(); return fingerprint((X509Certificate)store().getCertificate(ID)); }
    static String code(String pc) throws Exception {
        String[] ids={pc,id()}; Arrays.sort(ids); String h=hex(MessageDigest.getInstance("SHA-256").digest(("Lantern-pair-v1:"+ids[0]+":"+ids[1]).getBytes(java.nio.charset.StandardCharsets.UTF_8)));
        return String.join(" ",h.substring(0,4),h.substring(4,8),h.substring(8,12),h.substring(12,16),h.substring(16,20),h.substring(20,24));
    }
    static SSLSocketFactory tls(String pin) throws Exception {
        ensure(); KeyStore ks=store(); PrivateKey key=(PrivateKey)ks.getKey(ID,null); X509Certificate cert=(X509Certificate)ks.getCertificate(ID);
        X509ExtendedKeyManager km=new X509ExtendedKeyManager(){
            public String[] getClientAliases(String type,java.security.Principal[] issuers){return type.equals("EC")?new String[]{ID}:null;}
            public String chooseClientAlias(String[] types,java.security.Principal[] issuers,Socket socket){return Arrays.asList(types).contains("EC")?ID:null;}
            public String chooseEngineClientAlias(String[] types,java.security.Principal[] issuers,SSLEngine engine){return Arrays.asList(types).contains("EC")?ID:null;}
            public String[] getServerAliases(String type,java.security.Principal[] issuers){return null;}
            public String chooseServerAlias(String type,java.security.Principal[] issuers,Socket socket){return null;}
            public X509Certificate[] getCertificateChain(String alias){return new X509Certificate[]{cert};}
            public PrivateKey getPrivateKey(String alias){return key;}
        };
        X509TrustManager tm=new X509TrustManager(){
            public void checkClientTrusted(X509Certificate[] c,String t) throws java.security.cert.CertificateException {throw new java.security.cert.CertificateException();}
            public void checkServerTrusted(X509Certificate[] chain,String type) throws java.security.cert.CertificateException {
                try { if(chain.length==0||!MessageDigest.isEqual(pin.getBytes(java.nio.charset.StandardCharsets.US_ASCII),fingerprint(chain[0]).getBytes(java.nio.charset.StandardCharsets.US_ASCII)))throw new java.security.cert.CertificateException("PC khác mã đã ghép đôi"); }
                catch(java.security.cert.CertificateException e){throw e;} catch(Exception e){throw new java.security.cert.CertificateException(e);}
            }
            public X509Certificate[] getAcceptedIssuers(){return new X509Certificate[0];}
        };
        SSLContext ssl=SSLContext.getInstance("TLS"); ssl.init(new KeyManager[]{km},new TrustManager[]{tm},new SecureRandom()); return ssl.getSocketFactory();
    }
    static void save(Context context,JSONObject config) throws Exception {
        ensure(); Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding"); cipher.init(Cipher.ENCRYPT_MODE,store().getKey(SECRET,null));
        byte[] data=cipher.doFinal(config.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8));
        String value=Base64.encodeToString(cipher.getIV(),Base64.NO_WRAP)+":"+Base64.encodeToString(data,Base64.NO_WRAP);
        context.getSharedPreferences("private",0).edit().putString("connection",value).commit();
    }
    static JSONObject load(Context context) throws Exception {
        String value=context.getSharedPreferences("private",0).getString("connection",null); if(value==null)return null;
        String[] parts=value.split(":",2); Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding"); cipher.init(Cipher.DECRYPT_MODE,store().getKey(SECRET,null),new GCMParameterSpec(128,Base64.decode(parts[0],Base64.NO_WRAP)));
        return new JSONObject(new String(cipher.doFinal(Base64.decode(parts[1],Base64.NO_WRAP)),java.nio.charset.StandardCharsets.UTF_8));
    }
    static void forget(Context context) {context.getSharedPreferences("private",0).edit().remove("connection").commit();}
}
