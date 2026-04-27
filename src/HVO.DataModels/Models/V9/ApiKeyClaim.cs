using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

[Table("ApiKeyClaim", Schema = "v9")]
public class ApiKeyClaim
{
    [Key]
    public int Id { get; set; }

    public Guid ApiKeyId { get; set; }

    [ForeignKey(nameof(ApiKeyId))]
    public ApiKey ApiKey { get; set; } = null!;

    /// <summary>
    /// Claim type. Well-known values:
    ///   "scope"      — e.g. "ingest:weather", "ingest:images", "read:weather"
    ///   "station_id" — e.g. "davis-01"
    ///   "role"       — e.g. "ApiUser"
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string ClaimType { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string ClaimValue { get; set; } = string.Empty;
}
