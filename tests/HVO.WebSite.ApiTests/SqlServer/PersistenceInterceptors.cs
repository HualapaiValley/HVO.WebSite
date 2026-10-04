using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HVO.WebSite.ApiTests.SqlServer;

/// <summary>Runs a competing context after the application's lookups and before its first write.</summary>
internal sealed class BeforeFirstSaveInterceptor(Func<CancellationToken, Task> competingWrite) : SaveChangesInterceptor
{
    private int invoked;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref invoked, 1) == 0)
            await competingWrite(cancellationToken);
        return result;
    }
}

internal sealed class CaptureSaveFailureInterceptor : SaveChangesInterceptor
{
    public Exception? Failure { get; private set; }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Failure = eventData.Exception;
        return Task.CompletedTask;
    }
}
