using System;
using System.Collections.Generic;

namespace HVO.DataModels.Models;

public partial class SecurityCameraRecord
{
    public int Id { get; set; }

    public DateTimeOffset RecordDateTime { get; set; }

    public byte ImageType { get; set; }

    public byte CameraNumber { get; set; }

    public string StorageLocation { get; set; } = null!;
}
