using System.Text;

namespace HVO.Gateway.TplinkKasa.Protocol;

public static class KasaXorCipher
{
    private const byte InitialKey = 171;

    public static byte[] Encrypt(ReadOnlySpan<byte> plainText)
    {
        var output = new byte[plainText.Length];
        var key = InitialKey;

        for (var i = 0; i < plainText.Length; i++)
        {
            var encrypted = (byte)(plainText[i] ^ key);
            key = encrypted;
            output[i] = encrypted;
        }

        return output;
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> cipherText)
    {
        var output = new byte[cipherText.Length];
        var key = InitialKey;

        for (var i = 0; i < cipherText.Length; i++)
        {
            var encrypted = cipherText[i];
            output[i] = (byte)(encrypted ^ key);
            key = encrypted;
        }

        return output;
    }

    public static byte[] EncryptString(string json) => Encrypt(Encoding.UTF8.GetBytes(json));

    public static string DecryptToString(ReadOnlySpan<byte> cipherText) => Encoding.UTF8.GetString(Decrypt(cipherText));
}
