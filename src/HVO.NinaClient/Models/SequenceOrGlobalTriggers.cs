using HVO.Core.OneOf;
using HVO.Core.Results;

namespace HVO.NinaClient.Models;

[NamedOneOf("Sequence", typeof(SequenceResponse), "GlobalTriggers", typeof(GlobalTriggersResponse))]
public partial class SequenceOrGlobalTriggers;
