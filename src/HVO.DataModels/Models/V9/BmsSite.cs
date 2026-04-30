using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

[Table("BmsSite", Schema = "v9")]
public class BmsSite
{
    [Key]
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Location { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public ICollection<BmsDevice> Devices { get; set; } = [];
}
