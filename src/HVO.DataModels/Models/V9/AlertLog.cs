using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

[Table("AlertLog", Schema = "v9")]
public class AlertLog
{
    [Key]
    public long Id { get; set; }

    public DateTime OccurredAt { get; set; }

    [Required]
    [MaxLength(64)]
    public string Severity { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Source { get; set; } = string.Empty;

    [Required]
    [MaxLength(1024)]
    public string Message { get; set; } = string.Empty;

    public bool Acknowledged { get; set; }

    public DateTime? AcknowledgedAt { get; set; }

    [MaxLength(256)]
    public string? AcknowledgedBy { get; set; }
}
