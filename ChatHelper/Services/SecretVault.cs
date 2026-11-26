using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ChatHelper.Services;

/// <summary>
/// GAS 웹앱 URL, API 토큰, 폴백 시트 URL을 AES-256으로 암호화해 보관하고 런타임에 복호화합니다.
/// 디컴파일러나 바이너리 문자열 검색 시 민감한 주소/토큰이 평문으로 노출되는 것을 방지합니다.
/// </summary>
public static class SecretVault
{
    private const string EncWebhook = "XYe+MeoGarvBLufmi0Q3iWu26oPmr9rr1wzO/Egs0V6Jal2LqQgwe/RjJ6UP9JdP3zBPusQVrekSpSrGsL+d0+yQwYzy5B+ccJKCNHJk9HZXLCFUZVH8Djj8dz4peLgbFN5mT4xV9H9Uw4QUIO2kt8j0k1MRt1/rzAOQgp9hgUrDSywewnYbGXU/HEoutjnC";
    private const string EncToken = "GbrORgTFIFKDGojDQ54iJOcsspDdLEn1PWtM2iVYsag=";
    private const string EncUserCsv = "Lf+B7gnpfpy509k0o4GFHyXGrXnZzgiq/qR3tFtPCStPabSDd87HlTUcMwNCnaOFlq1rEL/08p2fFF1HXERHCGVevxbrVpJ2j8tnUntANjXALkdWliZinb3GldF3by7kNhzlRPki4lSdFtjt4ugkvGRxmphG5arkVC0MYQqehbfDvpnKLr9Q27lRyUFI219Jt8dTSfWrbAilKPOuzmXEFW15Rq6JM5H4Z6Z0pAwN2Ls=";
    private const string EncSheetId = "09qQWgohheOignDEEzJdEugV8i25w27kvxFWkmFQMYPfUnsHIYemgQZW4bTJJoxaUVKXzCzsGwlGvwy26LMjSNxiApCPnk+PIJR3giyOtlVucRVzMr9xpQZWDnwYoOEe";

    // 키/IV를 바이트 배열로 분할 보관 (평문 문자열 검색 회피)
    private static readonly byte[] K1 = { 0x51, 0x53, 0x5F, 0x43, 0x68, 0x61, 0x74, 0x48, 0x65, 0x6C, 0x70, 0x65, 0x72, 0x5F, 0x32, 0x30 };
    private static readonly byte[] K2 = { 0x32, 0x36, 0x5F, 0x53, 0x65, 0x63, 0x72, 0x65, 0x74, 0x4B, 0x65, 0x79, 0x21, 0x23, 0x39, 0x39 };
    private static readonly byte[] Iv = { 0x51, 0x53, 0x5F, 0x43, 0x48, 0x5F, 0x49, 0x56, 0x5F, 0x32, 0x30, 0x32, 0x36, 0x5F, 0x31, 0x36 };

    public static string GasWebhookUrl => Decrypt(EncWebhook);
    public static string GasApiToken => Decrypt(EncToken);
    public static string FallbackUserSheetUrl => Decrypt(EncUserCsv);
    public static string FallbackPublishedSheetId => Decrypt(EncSheetId);

    private static string Decrypt(string base64Cipher)
    {
        try
        {
            byte[] key = new byte[32];
            Buffer.BlockCopy(K1, 0, key, 0, 16);
            Buffer.BlockCopy(K2, 0, key, 16, 16);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = Iv;
            using var dec = aes.CreateDecryptor();
            byte[] cipher = Convert.FromBase64String(base64Cipher);
            byte[] plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return string.Empty;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn,
        string? szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    /// <summary>
    /// Windows DPAPI(현재 Windows 사용자 계정 전용)로 민감 문자열을 암호화합니다.
    /// 다른 PC나 다른 사용자 계정으로 config.json이 유출되어도 복호화할 수 없습니다.
    /// </summary>
    public static string ProtectForCurrentUser(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        DATA_BLOB inBlob = new();
        DATA_BLOB outBlob = new();
        GCHandle handle = GCHandle.Alloc(plainBytes, GCHandleType.Pinned);
        try
        {
            inBlob.cbData = plainBytes.Length;
            inBlob.pbData = handle.AddrOfPinnedObject();
            if (!CryptProtectData(ref inBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0x01 /* CRYPTPROTECT_UI_FORBIDDEN */, out outBlob))
                return string.Empty;

            byte[] cipherBytes = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, cipherBytes, 0, outBlob.cbData);
            return Convert.ToBase64String(cipherBytes);
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            handle.Free();
            if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
        }
    }

    /// <summary>
    /// Windows DPAPI로 암호화된 Base64 문자열을 복호화합니다.
    /// </summary>
    public static string UnprotectForCurrentUser(string? protectedBase64)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64)) return string.Empty;
        try
        {
            byte[] cipherBytes = Convert.FromBase64String(protectedBase64);
            DATA_BLOB inBlob = new();
            DATA_BLOB outBlob = new();
            GCHandle handle = GCHandle.Alloc(cipherBytes, GCHandleType.Pinned);
            try
            {
                inBlob.cbData = cipherBytes.Length;
                inBlob.pbData = handle.AddrOfPinnedObject();
                if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0x01, out outBlob))
                    return string.Empty;

                byte[] plainBytes = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, plainBytes, 0, outBlob.cbData);
                return Encoding.UTF8.GetString(plainBytes);
            }
            finally
            {
                handle.Free();
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }
        catch
        {
            return string.Empty;
        }
    }
}
