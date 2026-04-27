using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

[Table("ApiKey", Schema = "v9")]
public class ApiKey
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>SHA-256 hash of the raw key value. The plaintext key is shown only once at creation.</summary>
    [Required]
    [MaxLength(64)]
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>Friendly label, e.g. "Davis Weather Station" or "John's Weather App".</summary>
    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    public ApiKeyType Type { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    /// <summary>FK to ApiKeyOwner — only populated for User-type keys.</summary>
    public Guid? OwnerId { get; set; }

    [ForeignKey(nameof(OwnerId))]
    public ApiKeyOwner? Owner { get; set; }

    public ICollection<ApiKeyClaim> Claims { get; set; } = [];
}
