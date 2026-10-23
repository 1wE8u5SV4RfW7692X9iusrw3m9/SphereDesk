using System.IO;
using System.Windows;
using System.Windows.Threading;
using SphereDesk.Application.Configuration;
using SphereDesk.Application.Desktop;
using SphereDesk.Windows.Integration;
using SphereDesk.Windows.Shell;

namespace SphereDesk.App;

public partial class App : System.Windows.Application
{
    private SingleInstance? _singleInstance;
    private DesktopSession? _session;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirst)
        {
            SingleInstance.SignalFirstInstance();
            Shutdown();
            return;
        }

        var synthetic = e.Args.Any(argument => string.Equals(argument, "--synthetic", StringComparison.OrdinalIgnoreCase));
        var store = new JsonConfigStore(synthetic
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SphereDesk", "synthetic")
            : null);
        var loaded = store.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (!loaded.Settings.MotionPreferenceUserSet)
        {
            loaded.Settings.ReducedMotion = !SystemMotion.AnimationsEnabled();
        }

        _session = synthetic
            ? new DesktopSession(loaded.Settings, store, new SyntheticDesktopSource(), new SyntheticDesktopSource(), new NullChangeFeed(), new PlaceholderIconProvider(), new RejectingLauncher(), true)
            : new DesktopSession(loaded.Settings, store, new KnownFolderLocator(), new DirectoryDesktopSource(), new WatcherChangeFeed(), new ShellIconProvider(), new ShellLauncher(), false);

        if (!synthetic)
        {
            StartupRegistration.Apply(loaded.Settings.LaunchAtStartup);
        }

        _mainWindow = new MainWindow(_session, synthetic);
        _singleInstance.StartServer(() => _mainWindow.Dispatcher.BeginInvoke(_mainWindow.BringToFront));
        MainWindow = _mainWindow;
        if (synthetic)
        {
            _mainWindow.Show();
        }
        else
        {
            _mainWindow.EnableDesktopMode();
        }
        if (!string.IsNullOrWhiteSpace(loaded.UserMessage))
        {
            MessageBox.Show(_mainWindow, loaded.UserMessage, "SphereDesk", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        var session = _session;
        _ = Task.Run(async () =>
        {
            try
            {
                await session.StartAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                AppLog.Write("StartFailed");
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_session is not null)
        {
            try
            {
                _session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                AppLog.Write("ExitFailed");
            }
        }

        _singleInstance?.Dispose();
        AppLog.Write("Exited");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write("UiUnhandled");
        MessageBox.Show("SphereDesk 遇到问题，这次操作没有完成。", "SphereDesk", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
