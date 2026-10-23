using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using SphereDesk.Application.Abstractions;
using SphereDesk.Core.Models;
using SphereDesk.Windows.Integration;
using SphereDesk.Windows.Interop;

namespace SphereDesk.Windows.Shell;

public sealed class ShellIconProvider : IIconProvider
{
    private readonly StaScheduler _scheduler = new();
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<CacheEntry> _order = new();
    private readonly string _directory;
    private readonly int _capacity;
    private bool _disposed;

    public ShellIconProvider(string? cacheDirectory = null, int capacity = 256)
    {
        _capacity = Math.Max(32, capacity);
        _directory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SphereDesk",
            "icon-cache");
        Directory.CreateDirectory(_directory);
    }

    public async Task<IconResult> GetAsync(DesktopItem item, int requestedPixelSize, double dpiScale, CancellationToken cancellationToken)
    {
        var pixelSize = Math.Clamp(requestedPixelSize, 16, 256);
        var dpiBucket = (int)Math.Round(double.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale * 96 : 96);
        var key = item.IconKey + "|" + pixelSize.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + dpiBucket.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (TryGetMemory(key, out var cached))
        {
            return cached;
        }

        if (TryReadDisk(key, out var stored))
        {
            Remember(key, stored);
            return stored;
        }

        IconResult rendered;
        try
        {
            rendered = await _scheduler.InvokeAsync(() => Render(item.OriginalPath, pixelSize), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            SphereDesk.Windows.Integration.AppLog.Write("IconFailed");
            return new IconResult(true, 0, 0, null, "暂时无法读取系统图标。");
        }

        if (!rendered.IsPlaceholder && rendered.BgraPixels is not null)
        {
            Remember(key, rendered);
            TryWriteDisk(key, rendered);
        }

        return rendered;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scheduler.Dispose();
    }

    private static IconResult Render(string path, int pixelSize)
    {
        var iid = NativeMethods.ShellItemImageFactoryId;
        var hr = NativeMethods.SHCreateItemFromParsingName(path, 0, ref iid, out var unknown);
        if (hr < 0 || unknown == 0)
        {
            return new IconResult(true, 0, 0, null, "暂时无法读取系统图标。");
        }

        object? com = null;
        nint bitmap = 0;
        try
        {
            com = Marshal.GetObjectForIUnknown(unknown);
            if (com is not IShellItemImageFactory factory)
            {
                return new IconResult(true, 0, 0, null, "暂时无法读取系统图标。");
            }

            var size = new NativeMethods.Size { Width = pixelSize, Height = pixelSize };
            hr = factory.GetImage(size, NativeMethods.SIIGBF_ICONONLY | NativeMethods.SIIGBF_BIGGERSIZEOK, out bitmap);
            if (hr < 0 || bitmap == 0)
            {
                return new IconResult(true, 0, 0, null, "暂时无法读取系统图标。");
            }

            if (!TryCopyBitmap(bitmap, out var width, out var height, out var pixels))
            {
                return new IconResult(true, 0, 0, null, "暂时无法读取系统图标。");
            }

            return new IconResult(false, width, height, pixels, null);
        }
        finally
        {
            if (bitmap != 0)
            {
                NativeMethods.DeleteObject(bitmap);
            }

            if (com is not null)
            {
                Marshal.FinalReleaseComObject(com);
            }

            Marshal.Release(unknown);
        }
    }

    private static bool TryCopyBitmap(nint bitmap, out int width, out int height, out byte[] pixels)
    {
        width = 0;
        height = 0;
        pixels = [];
        if (NativeMethods.GetObject(bitmap, Marshal.SizeOf<NativeMethods.Bitmap>(), out var description) == 0 || description.Width <= 0 || description.Height <= 0)
        {
            return false;
        }

        width = description.Width;
        height = description.Height;
        var info = new NativeMethods.BitmapInfo
        {
            Header = new NativeMethods.BitmapInfoHeader
            {
                Size = Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = NativeMethods.BI_RGB,
            },
        };
        pixels = new byte[width * height * 4];
        var dc = NativeMethods.GetDC(0);
        try
        {
            var rows = NativeMethods.GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref info, NativeMethods.DIB_RGB_COLORS);
            if (rows == 0)
            {
                info.Header.Height = height;
                rows = NativeMethods.GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref info, NativeMethods.DIB_RGB_COLORS);
                if (rows == 0)
                {
                    return false;
                }

                FlipVertical(pixels, width, height);
            }

            return true;
        }
        finally
        {
            if (dc != 0)
            {
                NativeMethods.ReleaseDC(0, dc);
            }
        }
    }

    private static void FlipVertical(byte[] pixels, int width, int height)
    {
        var stride = width * 4;
        var row = new byte[stride];
        for (var y = 0; y < height / 2; y++)
        {
            var top = y * stride;
            var bottom = (height - 1 - y) * stride;
            Buffer.BlockCopy(pixels, top, row, 0, stride);
            Buffer.BlockCopy(pixels, bottom, pixels, top, stride);
            Buffer.BlockCopy(row, 0, pixels, bottom, stride);
        }
    }

    private bool TryGetMemory(string key, out IconResult result)
    {
        lock (_cacheGate)
        {
            if (!_cache.TryGetValue(key, out var node))
            {
                result = new IconResult(true, 0, 0, null, null);
                return false;
            }

            _order.Remove(node);
            _order.AddFirst(node);
            result = node.Value.Result;
            return true;
        }
    }

    private void Remember(string key, IconResult result)
    {
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _cache.Remove(key);
            }

            var node = new LinkedListNode<CacheEntry>(new CacheEntry(key, result));
            _order.AddFirst(node);
            _cache[key] = node;
            while (_cache.Count > _capacity && _order.Last is { } last)
            {
                _order.RemoveLast();
                _cache.Remove(last.Value.Key);
            }
        }
    }

    private bool TryReadDisk(string key, out IconResult result)
    {
        result = new IconResult(true, 0, 0, null, null);
        var path = CachePath(key);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            var width = reader.ReadInt32();
            var height = reader.ReadInt32();
            if (width <= 0 || height <= 0 || width > 256 || height > 256)
            {
                return false;
            }

            var count = width * height * 4;
            var pixels = reader.ReadBytes(count);
            if (pixels.Length != count)
            {
                return false;
            }

            result = new IconResult(false, width, height, pixels, null);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void TryWriteDisk(string key, IconResult result)
    {
        if (result.BgraPixels is null)
        {
            return;
        }

        try
        {
            using var stream = new FileStream(CachePath(key), FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(stream);
            writer.Write(result.PixelWidth);
            writer.Write(result.PixelHeight);
            writer.Write(result.BgraPixels);
            stream.Flush(true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string CachePath(string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_directory, hash + ".bin");
    }

    private readonly record struct CacheEntry(string Key, IconResult Result);
}
