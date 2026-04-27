namespace HVO.DataModels.Models.V9;

public enum ApiKeyType
{
    /// <summary>
    /// System key — used by devices and automated ingest clients.
    /// Claims are fully defined in ApiKeyClaim rows.
    /// </summary>
    System = 0,

    /// <summary>
    /// User key — used by external developers/applications on behalf of a person.
    /// Claims come from ApiKeyClaim rows; ApiKeyOwner provides the linked Entra identity.
    /// </summary>
    User = 1
}
