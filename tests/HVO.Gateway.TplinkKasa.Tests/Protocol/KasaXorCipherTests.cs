using FluentAssertions;
using HVO.Gateway.TplinkKasa.Protocol;

namespace HVO.Gateway.TplinkKasa.Tests.Protocol;

[TestClass]
public sealed class KasaXorCipherTests
{
    [TestMethod]
    public void EncryptThenDecrypt_RoundTripsJson()
    {
        const string json = "{\"system\":{\"get_sysinfo\":{}}}";

        var encrypted = KasaXorCipher.EncryptString(json);
        var decrypted = KasaXorCipher.DecryptToString(encrypted);

        decrypted.Should().Be(json);
        encrypted.Should().NotEqual(System.Text.Encoding.UTF8.GetBytes(json));
    }

    [TestMethod]
    public void TcpFrame_HasBigEndianLengthPrefix()
    {
        const string json = "{\"system\":{\"get_sysinfo\":{}}}";

        var frame = KasaFrameCodec.EncodeTcpFrame(json);

        frame[0].Should().Be(0);
        frame[1].Should().Be(0);
        frame[2].Should().Be(0);
        frame[3].Should().Be((byte)(frame.Length - 4));
        KasaFrameCodec.DecodeTcpPayload(frame.AsSpan(4)).Should().Be(json);
    }
}
