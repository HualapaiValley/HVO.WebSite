using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

[Table("BmsCellResistance", Schema = "v9")]
public class BmsCellResistance
{
    public long ReadingId { get; set; }

    [ForeignKey(nameof(ReadingId))]
    public BmsReading? Reading { get; set; }

    /// <summary>1-based cell index.</summary>
    public byte CellIndex { get; set; }

    /// <summary>Internal cell resistance (mΩ).</summary>
    public int ResistanceMOhm { get; set; }
}
