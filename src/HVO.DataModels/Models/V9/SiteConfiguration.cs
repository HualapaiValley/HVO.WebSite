using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

[Table("SiteConfiguration", Schema = "v9")]
public class SiteConfiguration
{
    [Key]
    [MaxLength(128)]
    public string Key { get; set; } = string.Empty;

    [Required]
    [MaxLength(2048)]
    public string Value { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? Description { get; set; }

    public DateTime UpdatedAt { get; set; }

    [MaxLength(256)]
    public string? UpdatedBy { get; set; }
}
