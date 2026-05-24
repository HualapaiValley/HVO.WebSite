using HVO.Hardware.JkBms.Protocol.Transport;
using Linux.Bluetooth;

namespace HVO.Hardware.JkBms.Tests.Fakes;

internal sealed class FakeBluetoothAdapterCoordinator : IBluetoothAdapterCoordinator
{
    private readonly Queue<Func<CancellationToken, Task>> _steps = new();

    public int ConnectCallCount { get; private set; }
    public List<(string AdapterName, string Address)> Requests { get; } = [];

    public void EnqueueSuccess() => _steps.Enqueue(_ => Task.CompletedTask);

    public void EnqueueFailure(Exception ex) => _steps.Enqueue(_ => Task.FromException(ex));

    public Task ConnectAsync(
        string adapterName,
        string address,
        Func<Device, CancellationToken, Task> connectAsync,
        CancellationToken ct)
    {
        ConnectCallCount++;
        Requests.Add((adapterName, address));

        return RunAsync(connectAsync, ct);
    }

    private async Task RunAsync(Func<Device, CancellationToken, Task> connectAsync, CancellationToken ct)
    {
        if (_steps.Count > 0)
            await _steps.Dequeue()(ct);

        await connectAsync(null!, ct);
    }
}
