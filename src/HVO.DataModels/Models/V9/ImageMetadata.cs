using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

[Table("ImageMetadata", Schema = "v9")]
public class ImageMetadata
{
    [Key]
    public long Id { get; set; }

    public DateTime CapturedAt { get; set; }

    [Required]
    [MaxLength(512)]
    public string BlobPath { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? CameraId { get; set; }

    [MaxLength(32)]
    public string? ImageType { get; set; }

    public long? FileSizeBytes { get; set; }

    public int? WidthPx { get; set; }

    public int? HeightPx { get; set; }
}
