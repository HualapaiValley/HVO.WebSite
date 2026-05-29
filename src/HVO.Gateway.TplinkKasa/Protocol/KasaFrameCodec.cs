using System.Buffers.Binary;

namespace HVO.Gateway.TplinkKasa.Protocol;

public static class KasaFrameCodec
{
    public static byte[] EncodeTcpFrame(string json)
    {
        var encrypted = KasaXorCipher.EncryptString(json);
        var frame = new byte[encrypted.Length + 4];
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, 4), encrypted.Length);
        encrypted.CopyTo(frame.AsSpan(4));
        return frame;
    }

    public static string DecodeTcpPayload(ReadOnlySpan<byte> encryptedPayload) =>
        KasaXorCipher.DecryptToString(encryptedPayload);
}
