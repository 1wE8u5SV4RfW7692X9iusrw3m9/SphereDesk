using SphereDesk.Application.Abstractions;
using SphereDesk.Core.Merge;
using SphereDesk.Core.Models;

namespace SphereDesk.Windows.Shell;

public sealed class WatcherChangeFeed : IDesktopChangeFeed
{
    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = [];

    public event EventHandler? Changed;
    public event EventHandler<ExplicitRename>? Renamed;
    public event EventHandler? Overflowed;

    public void Watch(IReadOnlyList<SourceRoot> roots)
    {
        lock (_gate)
        {
            StopCore();
            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root.ResolvedPath) || !Directory.Exists(root.ResolvedPath))
                {
                    continue;
                }

                var watcher = new FileSystemWatcher(root.ResolvedPath)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Attributes | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                    EnableRaisingEvents = false,
                };
                var rootId = root.Id;
                watcher.Created += OnChanged;
                watcher.Deleted += OnChanged;
                watcher.Changed += OnChanged;
                watcher.Renamed += (_, args) =>
                {
                    Renamed?.Invoke(this, new ExplicitRename(rootId, args.OldFullPath, args.FullPath));
                    Changed?.Invoke(this, EventArgs.Empty);
                };
                watcher.Error += (_, _) => Overflowed?.Invoke(this, EventArgs.Empty);
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            StopCore();
        }
    }

    private void OnChanged(object? sender, FileSystemEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    private void StopCore()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
    }
}
