using System.Security.Cryptography;
using SphereDesk.Core.Models;
using SphereDesk.Windows.Shell;

namespace SphereDesk.Windows.Tests;

public class DesktopIntegrationTests
{
    [Fact]
    public async Task ScanReadsTopLevelNamesWithoutWriting()
    {
        var root = CreateTemp();
        var nested = Path.Combine(root, "子文件夹");
        Directory.CreateDirectory(nested);
        var files = new[]
        {
            Path.Combine(root, "你好 世界.txt"),
            Path.Combine(root, "emoji 😀.txt"),
            Path.Combine(root, "a.txt"),
            Path.Combine(nested, "隐藏的子文件.txt"),
        };
        foreach (var file in files)
        {
            await File.WriteAllTextAsync(file, file);
        }

        await File.WriteAllTextAsync(Path.Combine(root, "链接.url"), "[InternetShortcut]\r\nURL=https://example.com/\r\n");
        await File.WriteAllBytesAsync(Path.Combine(root, "损坏.lnk"), [1, 2, 3, 4]);
        await File.WriteAllTextAsync(Path.Combine(root, "desktop.ini"), "[.ShellClassInfo]\r\n");
        CreateShortcut(Path.Combine(root, "启动.lnk"), files[0]);
        var before = Fingerprint(root);
        var source = new DirectoryDesktopSource();
        var snapshot = await source.ScanAsync([Root(root)], 1, CancellationToken.None);
        var after = Fingerprint(root);
        Assert.Equal(before, after);
        Assert.Equal(ScanStatus.Succeeded, snapshot.Roots.Single().LastScanStatus);
        Assert.Contains(snapshot.Items, item => item.DisplayName == "你好 世界.txt" && item.Kind == ItemKind.File);
        Assert.Contains(snapshot.Items, item => item.DisplayName == "emoji 😀.txt");
        Assert.Contains(snapshot.Items, item => item.DisplayName == "a.txt");
        Assert.Contains(snapshot.Items, item => item.DisplayName == "子文件夹" && item.Kind == ItemKind.Folder);
        Assert.Contains(snapshot.Items, item => item.DisplayName == "链接.url" && item.Kind == ItemKind.InternetShortcut);
        Assert.Contains(snapshot.Items, item => item.DisplayName == "损坏.lnk" && item.Kind == ItemKind.Shortcut);
        Assert.Contains(snapshot.Items, item => item.DisplayName == "启动.lnk" && item.Kind == ItemKind.Shortcut);
        Assert.DoesNotContain(snapshot.Items, item => item.DisplayName == "隐藏的子文件.txt");
        Assert.DoesNotContain(snapshot.Items, item => item.DisplayName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SameNameInDifferentRootsStayDistinctAndMissingRootIsNotEmpty()
    {
        var first = CreateTemp();
        var second = CreateTemp();
        await File.WriteAllTextAsync(Path.Combine(first, "a.txt"), "one");
        await File.WriteAllTextAsync(Path.Combine(second, "a.txt"), "two");
        var source = new DirectoryDesktopSource();
        var snapshot = await source.ScanAsync([Root(first, "one"), Root(second, "two")], 2, CancellationToken.None);
        Assert.Equal(2, snapshot.Items.Count(item => item.DisplayName == "a.txt"));
        Assert.Equal(2, snapshot.Items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());

        var missing = await source.ScanAsync([Root(Path.Combine(first, "does-not-exist"))], 3, CancellationToken.None);
        Assert.Equal(ScanStatus.Failed, missing.Roots.Single().LastScanStatus);
        Assert.Empty(missing.Items);
    }

    [Fact]
    public async Task EmptyDirectoryIsSuccessfulAndNotAFailure()
    {
        var root = CreateTemp();
        var snapshot = await new DirectoryDesktopSource().ScanAsync([Root(root)], 4, CancellationToken.None);
        Assert.Equal(ScanStatus.Succeeded, snapshot.Roots.Single().LastScanStatus);
        Assert.Empty(snapshot.Items);
    }

    [Fact]
    public async Task WatcherObservesTopLevelCreate()
    {
        var root = CreateTemp();
        using var feed = new WatcherChangeFeed();
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        feed.Changed += (_, _) => seen.TrySetResult();
        feed.Watch([Root(root)]);
        await Task.Delay(200);
        await File.WriteAllTextAsync(Path.Combine(root, "新增.txt"), "x");
        var completed = await Task.WhenAny(seen.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(seen.Task, completed);
    }

    [Fact]
    public async Task IconProviderReturnsRealPixelsForTempFile()
    {
        var root = CreateTemp();
        var file = Path.Combine(root, "你好.txt");
        await File.WriteAllTextAsync(file, "hello");
        var item = DesktopEntryInspector.CreateItem("temp", file);
        using var provider = new ShellIconProvider(Path.Combine(root, "cache"));
        var result = await provider.GetAsync(item, 32, 1, CancellationToken.None);
        Assert.False(result.IsPlaceholder);
        Assert.NotNull(result.BgraPixels);
        Assert.True(result.BgraPixels!.Length == result.PixelWidth * result.PixelHeight * 4);
        Assert.Contains(result.BgraPixels, value => value != 0);
    }

    [Fact]
    public async Task MissingItemOpenDoesNotThrow()
    {
        var item = new DesktopItem("id", "temp", Path.Combine(CreateTemp(), "missing.txt"), "missing.txt", ItemKind.File, "key", false, ItemAvailability.Missing, 0);
        var result = await new ShellLauncher().OpenAsync(item, 0, CancellationToken.None);
        Assert.Equal(OperationStatus.Failure, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.UserMessage));
    }

    [Fact]
    public void KnownFoldersResolveUserDesktop()
    {
        var roots = new KnownFolderLocator().Resolve();
        var user = roots.Single(root => root.KnownFolderKind == KnownFolderKind.UserDesktop);
        var publicDesktop = roots.Single(root => root.KnownFolderKind == KnownFolderKind.PublicDesktop);
        Assert.False(string.IsNullOrWhiteSpace(user.ResolvedPath));
        Assert.True(Directory.Exists(user.ResolvedPath));
        Assert.Equal(2, roots.Count);
        Assert.NotNull(publicDesktop);
    }

    private static SourceRoot Root(string path, string id = "temp") =>
        new(id, KnownFolderKind.UserDesktop, path, ScanStatus.NotScanned, null);

    private static string CreateTemp()
    {
        var path = Path.Combine(Path.GetTempPath(), "SphereDeskWin" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Fingerprint(string root)
    {
        var entries = Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var entry in entries)
        {
            var relative = Path.GetRelativePath(root, entry);
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(relative + "|"));
            if (File.Exists(entry))
            {
                hash.AppendData(File.ReadAllBytes(entry));
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void CreateShortcut(string shortcutPath, string targetPath)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell 不可用。");
                dynamic shell = Activator.CreateInstance(type) ?? throw new InvalidOperationException("无法创建 WScript.Shell。");
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                shortcut.Save();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }
}
