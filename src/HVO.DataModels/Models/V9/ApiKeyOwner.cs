using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

[Table("ApiKeyOwner", Schema = "v9")]
public class ApiKeyOwner
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>Entra ID object ID of the linked user account.</summary>
    [Required]
    [MaxLength(64)]
    public string EntraObjectId { get; set; } = string.Empty;

    /// <summary>Cached display name from Entra — for display and audit without AD lookups.</summary>
    [MaxLength(256)]
    public string? DisplayName { get; set; }

    /// <summary>Cached email/UPN from Entra.</summary>
    [MaxLength(256)]
    public string? Email { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<ApiKey> ApiKeys { get; set; } = [];
}
