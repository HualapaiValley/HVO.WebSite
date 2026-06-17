namespace HVO.Hardware.JkBms.Outbox.Forwarders;

public sealed class PermanentForwarderException : Exception
{
    public PermanentForwarderException(IReadOnlyList<(long RecordId, string Error)> failedRecords)
        : base("One or more outbox records are permanently dead-lettered.")
    {
        FailedRecords = failedRecords;
    }

    public IReadOnlyList<(long RecordId, string Error)> FailedRecords { get; }
}
