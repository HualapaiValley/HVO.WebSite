using HVO.Core.OneOf;
using HVO.Core.Results;

namespace HVO.NinaClient.Models;

[NamedOneOf("CaptureResponse", typeof(CaptureResponse), "StringResponse", typeof(string))]
public partial class CaptureResponseOrString;
