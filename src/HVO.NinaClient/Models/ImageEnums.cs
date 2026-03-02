using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Represents image type enumeration for filtering
/// </summary>
public enum ImageType
{
    LIGHT,
    FLAT,
    DARK,
    BIAS,
    SNAPSHOT
}

/// <summary>
/// Represents bayer pattern enumeration for debayering
/// </summary>
public enum BayerPattern
{
    Monochrome,
    Color,
    RGGB,
    CMYG,
    CMYG2,
    LRGB,
    BGGR,
    GBRG,
    GRBG,
    GRGB,
    GBGR,
    RGBG,
    BGRG
}
