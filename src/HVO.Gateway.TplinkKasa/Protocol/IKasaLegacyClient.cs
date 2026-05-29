using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Protocol;

public interface IKasaLegacyClient
{
    Task<JsonDocument> SendReadOnlyAsync(string host, int port, string commandJson, CancellationToken cancellationToken);
}
