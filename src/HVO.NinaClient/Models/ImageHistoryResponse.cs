using HVO.Core.OneOf;
using HVO.Core.Results;

namespace HVO.NinaClient.Models;

[NamedOneOf("HistoryItems", typeof(List<ImageHistoryItem>), "Count", typeof(int))]
public partial class ImageHistoryResponse;
