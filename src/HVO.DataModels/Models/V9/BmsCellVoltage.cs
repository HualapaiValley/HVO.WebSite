using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

[Table("BmsCellVoltage", Schema = "v9")]
public class BmsCellVoltage
{
    public long ReadingId { get; set; }

    [ForeignKey(nameof(ReadingId))]
    public BmsReading? Reading { get; set; }

    /// <summary>1-based cell index.</summary>
    public byte CellIndex { get; set; }

    /// <summary>Cell voltage (mV).</summary>
    public int VoltageMv { get; set; }
}
