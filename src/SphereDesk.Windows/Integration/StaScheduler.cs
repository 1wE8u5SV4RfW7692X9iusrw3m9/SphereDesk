using System.Collections.Concurrent;
using SphereDesk.Windows.Interop;

namespace SphereDesk.Windows.Integration;

public sealed class StaScheduler : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private volatile bool _stop;

    public StaScheduler()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "SphereDesk.IconSta",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> InvokeAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_stop)
        {
            completion.TrySetCanceled(cancellationToken);
            return completion.Task;
        }

        try
        {
            _queue.Add(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    completion.TrySetResult(work());
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            completion.TrySetCanceled(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            completion.TrySetCanceled(cancellationToken);
        }

        return completion.Task;
    }

    public void Dispose()
    {
        if (_stop)
        {
            return;
        }

        _stop = true;
        try
        {
            _queue.Add(() => { });
        }
        catch (InvalidOperationException)
        {
        }

        _thread.Join(TimeSpan.FromSeconds(3));
    }

    private void Run()
    {
        NativeMethods.CoInitializeEx(0, NativeMethods.COINIT_APARTMENTTHREADED);
        try
        {
            while (!_stop || _queue.Count > 0)
            {
                Pump();
                try
                {
                    if (_queue.TryTake(out var work, 15))
                    {
                        work();
                    }
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                if (_stop && _queue.Count == 0)
                {
                    break;
                }
            }
        }
        finally
        {
            NativeMethods.CoUninitialize();
        }
    }

    private static void Pump()
    {
        while (NativeMethods.PeekMessage(out var message, 0, 0, 0, 1))
        {
            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }
    }
}
