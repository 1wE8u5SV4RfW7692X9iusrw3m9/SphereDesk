using System.IO.Pipes;

namespace SphereDesk.Windows.Integration;

public sealed class SingleInstance : IDisposable
{
    public const string MutexName = @"Local\SphereDesk.SingleInstance.v1";
    public const string PipeName = "SphereDesk.Activate.v1";

    private readonly Mutex _mutex;
    private CancellationTokenSource? _cancellation;
    private Task? _listen;

    public SingleInstance()
    {
        _mutex = new Mutex(false, MutexName);
        try
        {
            IsFirst = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            IsFirst = true;
        }
    }

    public bool IsFirst { get; }

    public void StartServer(Action activated)
    {
        if (!IsFirst)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        _listen = Task.Run(() => ListenAsync(token, activated), token);
    }

    public static void SignalFirstInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
        }
        catch (TimeoutException)
        {
        }
        catch (IOException)
        {
        }
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        try
        {
            _listen?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch (AggregateException)
        {
        }

        _cancellation?.Dispose();
        if (IsFirst)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _mutex.Dispose();
    }

    private static async Task ListenAsync(CancellationToken cancellationToken, Action activated)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                activated();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
            }
        }
    }
}
