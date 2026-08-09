using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HVO.Hardware.Eg4.Protocol;

public sealed class Eg46500ExHidrawTransportFactory(TimeProvider timeProvider) : IEg46500ExInquiryTransportFactory
{
    public IEg46500ExInquiryTransport Create(string port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(port);
        try
        {
            return new Eg46500ExHidrawTransport(LinuxHidrawDevice.Open(port), timeProvider, TimeSpan.FromSeconds(2));
        }
        catch (IOException exception)
        {
            throw new Eg4TransportException(
                Eg4TransportFailureKind.Disconnected,
                "The 6500EX HID endpoint could not be opened.",
                exception);
        }
    }
}

public sealed class Eg46500ExHidrawTransport : IEg46500ExInquiryTransport
{
    private const int HidReportSize = 8;
    private const int MaximumResponseSize = 512;
    private readonly IEg46500ExHidrawDevice _device;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lifecycleLock = new();
    private TaskCompletionSource _idle = CompletedSource();
    private Task? _disposeTask;
    private int _activeOperations;
    private bool _disposing;

    internal Eg46500ExHidrawTransport(
        IEg46500ExHidrawDevice device,
        TimeProvider timeProvider,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        _device = device;
        _timeProvider = timeProvider;
        _timeout = timeout;
    }

    public async ValueTask<byte[]> ExchangeAsync(Eg46500ExInquiry inquiry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeginOperation();
        try
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                using var timeout = new CancellationTokenSource(_timeout, _timeProvider);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                try
                {
                    return await Task.Run(() => ExchangeCore(inquiry, linked.Token), CancellationToken.None);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
                {
                    throw new Eg4TransportException(Eg4TransportFailureKind.Timeout, "The 6500EX inquiry timed out.");
                }
                catch (IOException exception)
                {
                    throw new Eg4TransportException(
                        Eg4TransportFailureKind.Disconnected,
                        "The 6500EX HID exchange failed.",
                        exception);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            EndOperation();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleLock)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private byte[] ExchangeCore(Eg46500ExInquiry inquiry, CancellationToken cancellationToken)
    {
        _device.Write(Eg46500ExPi30Protocol.Encode(inquiry), cancellationToken);
        var response = new List<byte>();
        var buffer = new byte[HidReportSize];
        while (response.Count < MaximumResponseSize)
        {
            var count = _device.Read(buffer, cancellationToken);
            if (count == 0)
                throw new Eg4TransportException(Eg4TransportFailureKind.Disconnected, "The 6500EX HID connection closed before a complete response.");
            for (var index = 0; index < count; index++)
            {
                response.Add(buffer[index]);
                if (buffer[index] == 0x0D)
                    return [.. response];
                if (response.Count == MaximumResponseSize)
                    break;
            }
        }
        throw new Eg4TransportException(Eg4TransportFailureKind.MalformedFrame, "The 6500EX response exceeded 512 bytes without CR framing.");
    }

    private async Task DisposeCoreAsync()
    {
        Task idleTask;
        lock (_lifecycleLock)
        {
            _disposing = true;
            idleTask = _idle.Task;
        }
        await idleTask;
        _device.Dispose();
        _gate.Dispose();
    }

    private void BeginOperation()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposing, this);
            if (_activeOperations++ == 0)
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void EndOperation()
    {
        lock (_lifecycleLock)
        {
            if (--_activeOperations == 0) _idle.TrySetResult();
        }
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}

internal interface IEg46500ExHidrawDevice : IDisposable
{
    void Write(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken);
    int Read(Span<byte> buffer, CancellationToken cancellationToken);
}

internal sealed class LinuxHidrawDevice : IEg46500ExHidrawDevice
{
    private const int OpenReadWrite = 0x0002;
    private const int OpenNonBlocking = 0x0800;
    private const short PollInput = 0x0001;
    private const short PollOutput = 0x0004;
    private const short PollError = 0x0008;
    private const short PollHangup = 0x0010;
    private const short PollInvalid = 0x0020;
    private const int InterruptedSystemCall = 4;
    private const int TryAgain = 11;
    private readonly SafeFileHandle _handle;

    private LinuxHidrawDevice(SafeFileHandle handle) => _handle = handle;

    public static LinuxHidrawDevice Open(string path)
    {
        var descriptor = NativeMethods.Open(path, OpenReadWrite | OpenNonBlocking);
        if (descriptor < 0)
            throw CreateIOException("open");
        return new LinuxHidrawDevice(new SafeFileHandle(descriptor, ownsHandle: true));
    }

    public void Write(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken)
    {
        var remaining = bytes.ToArray();
        while (remaining.Length > 0)
        {
            WaitFor(PollOutput, cancellationToken);
            var count = NativeMethods.Write(_handle, remaining, (nuint)remaining.Length);
            if (count < 0)
            {
                if (Marshal.GetLastPInvokeError() == TryAgain) continue;
                throw CreateIOException("write");
            }
            if (count == 0) throw new IOException("The hidraw write returned zero bytes.");
            remaining = remaining[(int)count..];
        }
    }

    public int Read(Span<byte> buffer, CancellationToken cancellationToken)
    {
        var bytes = new byte[buffer.Length];
        while (true)
        {
            WaitFor(PollInput, cancellationToken);
            var count = NativeMethods.Read(_handle, bytes, (nuint)bytes.Length);
            if (count < 0)
            {
                if (Marshal.GetLastPInvokeError() == TryAgain) continue;
                throw CreateIOException("read");
            }
            bytes.AsSpan(0, (int)count).CopyTo(buffer);
            return (int)count;
        }
    }

    public void Dispose() => _handle.Dispose();

    private void WaitFor(short events, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = new PollDescriptor
            {
                FileDescriptor = _handle.DangerousGetHandle().ToInt32(),
                Events = events,
            };
            var result = NativeMethods.Poll(ref descriptor, 1, 50);
            if (result == 0) continue;
            if (result < 0)
            {
                if (Marshal.GetLastPInvokeError() == InterruptedSystemCall) continue;
                throw CreateIOException("poll");
            }
            if ((descriptor.ReturnedEvents & (PollError | PollHangup | PollInvalid)) != 0)
                throw new IOException($"The hidraw descriptor reported poll flags 0x{descriptor.ReturnedEvents:X}.");
            if ((descriptor.ReturnedEvents & events) != 0) return;
        }
    }

    private static IOException CreateIOException(string operation)
    {
        var error = new Win32Exception(Marshal.GetLastPInvokeError());
        return new IOException($"Linux hidraw {operation} failed: {error.Message}", error);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollDescriptor
    {
        public int FileDescriptor;
        public short Events;
        public short ReturnedEvents;
    }

    private static class NativeMethods
    {
        [DllImport("libc", EntryPoint = "open", SetLastError = true, CharSet = CharSet.Ansi)]
        internal static extern int Open(string path, int flags);

        [DllImport("libc", EntryPoint = "read", SetLastError = true)]
        internal static extern nint Read(SafeFileHandle descriptor, byte[] buffer, nuint count);

        [DllImport("libc", EntryPoint = "write", SetLastError = true)]
        internal static extern nint Write(SafeFileHandle descriptor, byte[] buffer, nuint count);

        [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
        internal static extern int Poll(ref PollDescriptor descriptors, nuint count, int timeoutMilliseconds);
    }
}
