using System.Runtime.InteropServices;
using System.Text;

namespace SphereDesk.Windows.Integration;

public static class DesktopLayerHost
{
    private const int GwlStyle = -16;
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExLayered = 0x00080000;
    private const int WsExNoRedirectionBitmap = 0x00200000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int GwlExStyle = -20;
    private const int GwlpHwndParent = -8;
    private const nint HwndBottom = 1;
    private const uint LwaColorKey = 0x00000001;
    private const int ColorKey = 0x00FF00FF;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint CsDblClks = 0x0008;

    public static bool TryAttach(nint window)
    {
        var host = FindHost();
        if (window == 0 || host == 0)
        {
            AppLog.Write("DesktopHostFailed");
            return false;
        }

        var style = GetWindowLong(window, GwlStyle);
        style &= ~WsPopup;
        style |= WsChild | WsVisible;
        SetWindowLong(window, GwlStyle, style);
        var extended = GetWindowLong(window, GwlExStyle);
        SetWindowLong(window, GwlExStyle, extended | WsExLayered);
        SetParent(window, host);
        if (GetParent(window) != host)
        {
            AppLog.Write("DesktopHostFailed", Marshal.GetLastWin32Error());
            return false;
        }

        ApplyColorKey(window);
        Fit(window);
        return true;
    }

    public static bool IsAttached(nint window)
    {
        if (window == 0)
        {
            return false;
        }

        var parent = GetParent(window);
        return parent != 0 && IsWindow(parent);
    }

    public static void Fit(nint window)
    {
        var parent = GetParent(window);
        if (parent == 0 || !GetClientRect(parent, out var rect))
        {
            return;
        }

        ApplyColorKey(window);
        SetWindowPos(window, 0, 0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top, SwpNoActivate | SwpShowWindow);
    }

    public static void SetIconViewVisible(bool visible)
    {
        var host = FindHost();
        if (host == 0)
        {
            return;
        }

        var view = FindWindowEx(host, 0, "SHELLDLL_DefView", null);
        if (view == 0)
        {
            return;
        }

        var list = FindWindowEx(view, 0, "SysListView32", null);
        if (!visible && list == 0)
        {
            return;
        }

        ShowWindow(view, 5);
        if (list != 0)
        {
            ShowWindow(list, visible ? 5 : 0);
        }
    }

    private static WindowProcedure? _surfaceProcedure;
    private static Action<uint, nint, nint>? _surfaceMessage;

    public static nint CreateSurface(Action<uint, nint, nint> onMessage)
    {
        var host = FindHost();
        if (host == 0 || !TryGetPlacement(host, out var x, out var y, out var width, out var height))
        {
            AppLog.Write("DesktopHostFailed");
            return 0;
        }

        _surfaceMessage = onMessage;
        _surfaceProcedure = SurfaceProc;
        var instance = GetModuleHandle(null);
        var windowClass = new WindowClass
        {
            Style = CsDblClks,
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(_surfaceProcedure),
            Instance = instance,
            ClassName = "SphereDesk.DesktopSurface",
        };
        RegisterClass(ref windowClass);
        var gpu = DesktopGpuPresenter.TryPrepare();
        var extended = (gpu ? WsExNoRedirectionBitmap | WsExToolWindow : WsExLayered | WsExToolWindow) | WsExNoActivate;
        var hwnd = CreateWindowEx(extended, "SphereDesk.DesktopSurface", "SphereDesk", WsPopup | WsVisible, x, y, width, height, 0, 0, instance, 0);
        if (hwnd == 0)
        {
            DesktopGpuPresenter.Dispose();
            AppLog.Write("DesktopHostFailed", Marshal.GetLastWin32Error());
            return 0;
        }

        if (gpu && !DesktopGpuPresenter.Bind(hwnd, width, height))
        {
            DestroyWindow(hwnd);
            DesktopGpuPresenter.Dispose();
            AppLog.Write("DesktopPresentCpu", DesktopGpuPresenter.LastError);
            hwnd = CreateWindowEx(WsExLayered | WsExToolWindow | WsExNoActivate, "SphereDesk.DesktopSurface", "SphereDesk", WsPopup | WsVisible, x, y, width, height, 0, 0, instance, 0);
            if (hwnd == 0)
            {
                AppLog.Write("DesktopHostFailed", Marshal.GetLastWin32Error());
                return 0;
            }
        }
        else if (gpu)
        {
            AppLog.Write("DesktopPresentGpu");
        }
        else
        {
            AppLog.Write("DesktopPresentCpu", DesktopGpuPresenter.LastError);
        }

        var owner = FindDesktopOwner();
        if (owner != 0)
        {
            SetWindowLongPtr(hwnd, GwlpHwndParent, owner);
        }

        SetWindowPos(hwnd, HwndBottom, x, y, width, height, SwpNoActivate | SwpShowWindow);
        return hwnd;
    }

    public static void PlaceForDesktop(nint window)
    {
        if (window == 0 || GetCapture() == window)
        {
            return;
        }

        if (!TryGetPlacement(window, out var x, out var y, out var width, out var height))
        {
            SetWindowPos(window, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
            return;
        }

        SetWindowPos(window, HwndBottom, x, y, width, height, SwpNoActivate | SwpShowWindow);
    }

    public static bool IsPrimaryButtonDown() => (GetAsyncKeyState(1) & 0x8000) != 0;

    public static void SetPointerCursor(bool hand)
    {
        _useHandCursor = hand;
        ApplyPointerCursor();
    }

    public static bool TryGetCursorInWindow(nint window, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (window == 0 || !GetCursorPos(out var cursor) || !GetWindowRect(window, out var bounds))
        {
            return false;
        }

        x = cursor.X - bounds.Left;
        y = cursor.Y - bounds.Top;
        return true;
    }

    public static bool TryGetScreenCursor(out int x, out int y)
    {
        x = 0;
        y = 0;
        if (!GetCursorPos(out var cursor))
        {
            return false;
        }

        x = cursor.X;
        y = cursor.Y;
        return true;
    }

    public static bool TryGetClientSize(nint window, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!GetClientRect(window, out var rect))
        {
            return false;
        }

        width = rect.Right - rect.Left;
        height = rect.Bottom - rect.Top;
        return width > 0 && height > 0;
    }

    public readonly struct DesktopSprite
    {
        public int X { get; init; }
        public int Y { get; init; }
        public int DestinationWidth { get; init; }
        public int DestinationHeight { get; init; }
        public int SourceWidth { get; init; }
        public int SourceHeight { get; init; }
        public byte Opacity { get; init; }
        public byte[]? Pixels { get; init; }
    }

    public static bool PresentSprites(nint window, int width, int height, IReadOnlyList<DesktopSprite> sprites, int ringX, int ringY, int ringRadius, int hitRadius, int selectX, int selectY, int selectRadius)
    {
        var count = width * height * 4;
        if (count <= 0)
        {
            return false;
        }

        if (DesktopGpuPresenter.IsReady)
        {
            return DesktopGpuPresenter.Draw(width, height, sprites, ringX, ringY, ringRadius, hitRadius, selectX, selectY, selectRadius);
        }

        if (_compose is null || _compose.Length != count)
        {
            _compose = new byte[count];
        }
        else
        {
            Array.Clear(_compose);
        }

        FillDisc(_compose, width, height, ringX, ringY, hitRadius);
        foreach (var sprite in sprites)
        {
            Blit(_compose, width, height, sprite);
        }

        if (selectRadius > 0)
        {
            DrawRing(_compose, width, height, selectX, selectY, selectRadius, 255, 122, 162, 247);
            DrawRing(_compose, width, height, selectX, selectY, Math.Max(1, selectRadius - 1), 255, 122, 162, 247);
        }

        if (!EnsurePresentBuffer(width, height))
        {
            return false;
        }

        Marshal.Copy(_compose, 0, _presentBits, count);
        var previous = SelectObject(_presentDc, _presentSection);
        var source = new POINT();
        var size = new SIZE { Width = width, Height = height };
        var blend = new BlendFunction { Operation = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        var screen = GetDC(0);
        var ok = UpdateLayeredWindow(window, screen, 0, ref size, _presentDc, ref source, 0, ref blend, 2);
        ReleaseDC(0, screen);
        SelectObject(_presentDc, previous);
        return ok;
    }

    public static bool Present(nint window, byte[] pixels, int width, int height)
    {
        if (!EnsurePresentBuffer(width, height))
        {
            return false;
        }

        Marshal.Copy(pixels, 0, _presentBits, Math.Min(pixels.Length, width * height * 4));
        var previous = SelectObject(_presentDc, _presentSection);
        var source = new POINT();
        var size = new SIZE { Width = width, Height = height };
        var blend = new BlendFunction { Operation = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        var screen = GetDC(0);
        var ok = UpdateLayeredWindow(window, screen, 0, ref size, _presentDc, ref source, 0, ref blend, 2);
        ReleaseDC(0, screen);
        SelectObject(_presentDc, previous);
        return ok;
    }

    public static void DestroySurface(nint window)
    {
        DesktopGpuPresenter.Dispose();
        ReleasePresentBuffer();
        if (window != 0)
        {
            DestroyWindow(window);
        }
    }

    private static byte[]? _compose;
    private static nint _presentDc;
    private static nint _presentSection;
    private static nint _presentBits;
    private static int _presentWidth;
    private static int _presentHeight;

    private static bool EnsurePresentBuffer(int width, int height)
    {
        if (_presentSection != 0 && _presentBits != 0 && _presentDc != 0 && _presentWidth == width && _presentHeight == height)
        {
            return true;
        }

        ReleasePresentBuffer();
        var header = new BitmapHeader
        {
            Size = Marshal.SizeOf<BitmapHeader>(),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
        };
        var screen = GetDC(0);
        _presentDc = CreateCompatibleDC(screen);
        _presentSection = CreateDIBSection(screen, ref header, 0, out _presentBits, 0, 0);
        ReleaseDC(0, screen);
        if (_presentSection == 0 || _presentBits == 0 || _presentDc == 0)
        {
            ReleasePresentBuffer();
            return false;
        }

        _presentWidth = width;
        _presentHeight = height;
        return true;
    }

    private static void Blit(byte[] canvas, int canvasWidth, int canvasHeight, DesktopSprite sprite)
    {
        var source = sprite.Pixels;
        if (source is null || sprite.Opacity == 0 || sprite.DestinationWidth < 1 || sprite.DestinationHeight < 1 || sprite.SourceWidth < 1 || sprite.SourceHeight < 1)
        {
            return;
        }

        if (sprite.SourceWidth != sprite.DestinationWidth || sprite.SourceHeight != sprite.DestinationHeight)
        {
            BlitFiltered(canvas, canvasWidth, canvasHeight, sprite, source);
            return;
        }

        var opacity = sprite.Opacity;
        var sourceWidth = sprite.SourceWidth;
        var sourceHeight = sprite.SourceHeight;
        var destinationWidth = sprite.DestinationWidth;
        var destinationHeight = sprite.DestinationHeight;
        var xStep = (sourceWidth << 16) / destinationWidth;
        var yStep = (sourceHeight << 16) / destinationHeight;
        var yAccumulator = 0;
        for (var y = 0; y < destinationHeight; y++)
        {
            var dy = sprite.Y + y;
            var sy = yAccumulator >> 16;
            yAccumulator += yStep;
            if ((uint)dy >= (uint)canvasHeight || (uint)sy >= (uint)sourceHeight)
            {
                continue;
            }

            var sourceRow = sy * sourceWidth;
            var xAccumulator = 0;
            for (var x = 0; x < destinationWidth; x++)
            {
                var dx = sprite.X + x;
                var sx = xAccumulator >> 16;
                xAccumulator += xStep;
                if ((uint)dx >= (uint)canvasWidth || (uint)sx >= (uint)sourceWidth)
                {
                    continue;
                }

                var sourceIndex = (sourceRow + sx) * 4;
                if ((uint)sourceIndex >= (uint)(source.Length - 3))
                {
                    continue;
                }

                var sourceAlpha = source[sourceIndex + 3];
                if (sourceAlpha == 0)
                {
                    continue;
                }

                var alpha = opacity == 255 ? sourceAlpha : sourceAlpha * opacity / 255;
                if (alpha == 0)
                {
                    continue;
                }

                var destinationIndex = ((dy * canvasWidth) + dx) * 4;
                var inverse = 255 - alpha;
                var sourceBlue = opacity == 255 ? source[sourceIndex] : source[sourceIndex] * opacity / 255;
                var sourceGreen = opacity == 255 ? source[sourceIndex + 1] : source[sourceIndex + 1] * opacity / 255;
                var sourceRed = opacity == 255 ? source[sourceIndex + 2] : source[sourceIndex + 2] * opacity / 255;
                canvas[destinationIndex] = (byte)(sourceBlue + (canvas[destinationIndex] * inverse / 255));
                canvas[destinationIndex + 1] = (byte)(sourceGreen + (canvas[destinationIndex + 1] * inverse / 255));
                canvas[destinationIndex + 2] = (byte)(sourceRed + (canvas[destinationIndex + 2] * inverse / 255));
                canvas[destinationIndex + 3] = (byte)(alpha + (canvas[destinationIndex + 3] * inverse / 255));
            }
        }
    }

    private readonly record struct ScaledKey(int SourceId, int SourceWidth, int SourceHeight, int Width, int Height);

    private static readonly Dictionary<ScaledKey, byte[]> _scaled = new();

    private static int Quantize(int size) => size < 4 ? Math.Max(1, size) : ((size + 2) / 4) * 4;

    private static void BlitFiltered(byte[] canvas, int canvasWidth, int canvasHeight, DesktopSprite sprite, byte[] source)
    {
        var width = Quantize(sprite.DestinationWidth);
        var height = Quantize(sprite.DestinationHeight);
        var pixels = GetScaled(source, sprite.SourceWidth, sprite.SourceHeight, width, height);
        Blit(canvas, canvasWidth, canvasHeight, new DesktopSprite
        {
            X = sprite.X + ((sprite.DestinationWidth - width) / 2),
            Y = sprite.Y + ((sprite.DestinationHeight - height) / 2),
            DestinationWidth = width,
            DestinationHeight = height,
            SourceWidth = width,
            SourceHeight = height,
            Opacity = sprite.Opacity,
            Pixels = pixels,
        });
    }

    private static byte[] GetScaled(byte[] source, int sourceWidth, int sourceHeight, int width, int height)
    {
        var key = new ScaledKey(source.GetHashCode(), sourceWidth, sourceHeight, width, height);
        if (_scaled.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var sy16 = (int)(((long)((2 * y) + 1) * sourceHeight << 15) / height) - (1 << 15);
            for (var x = 0; x < width; x++)
            {
                var sx16 = (int)(((long)((2 * x) + 1) * sourceWidth << 15) / width) - (1 << 15);
                SampleBilinear(source, sourceWidth, sourceHeight, sx16, sy16, out var blue, out var green, out var red, out var alpha);
                var index = ((y * width) + x) * 4;
                pixels[index] = (byte)blue;
                pixels[index + 1] = (byte)green;
                pixels[index + 2] = (byte)red;
                pixels[index + 3] = (byte)alpha;
            }
        }

        if (_scaled.Count > 800)
        {
            _scaled.Clear();
        }

        _scaled[key] = pixels;
        return pixels;
    }

    private static void SampleBilinear(byte[] source, int width, int height, int sx16, int sy16, out int blue, out int green, out int red, out int alpha)
    {
        var x0 = sx16 >> 16;
        var y0 = sy16 >> 16;
        var fx = sx16 & 0xFFFF;
        var fy = sy16 & 0xFFFF;
        if (x0 < 0)
        {
            x0 = 0;
            fx = 0;
        }

        if (y0 < 0)
        {
            y0 = 0;
            fy = 0;
        }

        if (x0 >= width)
        {
            x0 = width - 1;
            fx = 0;
        }

        if (y0 >= height)
        {
            y0 = height - 1;
            fy = 0;
        }

        var x1 = x0 + 1 < width ? x0 + 1 : x0;
        var y1 = y0 + 1 < height ? y0 + 1 : y0;
        if (x1 == x0)
        {
            fx = 0;
        }

        if (y1 == y0)
        {
            fy = 0;
        }

        var row0 = y0 * width;
        var row1 = y1 * width;
        var i00 = (row0 + x0) * 4;
        var i10 = (row0 + x1) * 4;
        var i01 = (row1 + x0) * 4;
        var i11 = (row1 + x1) * 4;
        if ((uint)i11 >= (uint)(source.Length - 3))
        {
            blue = 0;
            green = 0;
            red = 0;
            alpha = 0;
            return;
        }

        blue = LerpChannel(source[i00], source[i10], source[i01], source[i11], fx, fy);
        green = LerpChannel(source[i00 + 1], source[i10 + 1], source[i01 + 1], source[i11 + 1], fx, fy);
        red = LerpChannel(source[i00 + 2], source[i10 + 2], source[i01 + 2], source[i11 + 2], fx, fy);
        alpha = LerpChannel(source[i00 + 3], source[i10 + 3], source[i01 + 3], source[i11 + 3], fx, fy);
    }

    private static int LerpChannel(int c00, int c10, int c01, int c11, int fx, int fy)
    {
        var top = c00 + (((c10 - c00) * fx) >> 16);
        var bottom = c01 + (((c11 - c01) * fx) >> 16);
        var value = top + (((bottom - top) * fy) >> 16);
        return value < 0 ? 0 : value > 255 ? 255 : value;
    }

    private static byte[]? _hitRun;

    private static void FillDisc(byte[] buffer, int width, int height, int centerX, int centerY, int radius)
    {
        if (radius <= 0)
        {
            return;
        }

        var runBytes = ((radius * 2) + 1) * 4;
        if (_hitRun is null || _hitRun.Length < runBytes)
        {
            _hitRun = new byte[runBytes];
            for (var index = 3; index < _hitRun.Length; index += 4)
            {
                _hitRun[index] = 1;
            }
        }

        var radiusSquared = (long)radius * radius;
        var top = Math.Max(0, centerY - radius);
        var bottom = Math.Min(height - 1, centerY + radius);
        for (var y = top; y <= bottom; y++)
        {
            var dy = y - centerY;
            var remaining = radiusSquared - ((long)dy * dy);
            if (remaining < 0)
            {
                continue;
            }

            var dx = (int)Math.Sqrt(remaining);
            var left = Math.Max(0, centerX - dx);
            var right = Math.Min(width - 1, centerX + dx);
            if (right < left)
            {
                continue;
            }

            Buffer.BlockCopy(_hitRun, 0, buffer, ((y * width) + left) * 4, (right - left + 1) * 4);
        }
    }

    private static void DrawRing(byte[] buffer, int width, int height, int centerX, int centerY, int radius, byte alpha, byte red, byte green, byte blue)
    {
        if (radius <= 0 || alpha == 0)
        {
            return;
        }

        var blueByte = (byte)(blue * alpha / 255);
        var greenByte = (byte)(green * alpha / 255);
        var redByte = (byte)(red * alpha / 255);
        var x = radius;
        var y = 0;
        var error = 0;
        while (x >= y)
        {
            Plot(buffer, width, height, centerX + x, centerY + y, blueByte, greenByte, redByte, alpha);
            Plot(buffer, width, height, centerX + y, centerY + x, blueByte, greenByte, redByte, alpha);
            Plot(buffer, width, height, centerX - y, centerY + x, blueByte, greenByte, redByte, alpha);
            Plot(buffer, width, height, centerX - x, centerY + y, blueByte, greenByte, redByte, alpha);
            Plot(buffer, width, height, centerX - x, centerY - y, blueByte, greenByte, redByte, alpha);
            Plot(buffer, width, height, centerX - y, centerY - x, blueByte, greenByte, redByte, alpha);
            Plot(buffer, width, height, centerX + y, centerY - x, blueByte, greenByte, redByte, alpha);
            Plot(buffer, width, height, centerX + x, centerY - y, blueByte, greenByte, redByte, alpha);
            y++;
            error += 1 + (2 * y);
            if ((2 * (error - x)) + 1 > 0)
            {
                x--;
                error += 1 - (2 * x);
            }
        }
    }

    private static void Plot(byte[] buffer, int width, int height, int x, int y, byte blue, byte green, byte red, byte alpha)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height || alpha == 0)
        {
            return;
        }

        var index = ((y * width) + x) * 4;
        var inverse = 255 - alpha;
        buffer[index] = (byte)(blue + (buffer[index] * inverse / 255));
        buffer[index + 1] = (byte)(green + (buffer[index + 1] * inverse / 255));
        buffer[index + 2] = (byte)(red + (buffer[index + 2] * inverse / 255));
        buffer[index + 3] = (byte)(alpha + (buffer[index + 3] * inverse / 255));
    }

    private static void ReleasePresentBuffer()
    {
        if (_presentSection != 0)
        {
            DeleteObject(_presentSection);
            _presentSection = 0;
        }

        if (_presentDc != 0)
        {
            DeleteDC(_presentDc);
            _presentDc = 0;
        }

        _presentBits = 0;
        _presentWidth = 0;
        _presentHeight = 0;
    }

    private static nint SurfaceProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == 0x0084)
        {
            if (!DesktopGpuPresenter.IsReady)
            {
                return 1;
            }

            var hitX = (short)(lParam.ToInt64() & 0xFFFF);
            var hitY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
            var hit = new POINT { X = hitX, Y = hitY };
            if (!ScreenToClient(hwnd, ref hit))
            {
                return 1;
            }

            return DesktopGpuPresenter.Contains(hit.X, hit.Y) ? 1 : -1;
        }

        if (message == 0x0021)
        {
            return 3;
        }

        if (message == 0x0046)
        {
            var position = Marshal.PtrToStructure<WindowPos>(lParam);
            position.InsertAfter = HwndBottom;
            position.Flags &= ~SwpNoZOrder;
            position.Flags |= SwpNoActivate;
            Marshal.StructureToPtr(position, lParam, false);
            return DefWindowProc(hwnd, message, wParam, lParam);
        }

        if (message == 0x0020)
        {
            SetPointerCursor(true);
            return 1;
        }

        if (message is 0x0200 or 0x0201 or 0x0202 or 0x0203 or 0x0205)
        {
            if (message is 0x0201 or 0x0203)
            {
                SetCapture(hwnd);
            }
            else if (message == 0x0202)
            {
                ReleaseCapture();
            }

            _surfaceMessage?.Invoke(message, wParam, lParam);
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static bool _useHandCursor;
    private static nint _handCursorHandle;
    private static nint _arrowCursorHandle;

    private static void ApplyPointerCursor()
    {
        if (_handCursorHandle == 0)
        {
            _handCursorHandle = LoadCursor(0, 32649);
        }

        if (_arrowCursorHandle == 0)
        {
            _arrowCursorHandle = LoadCursor(0, 32512);
        }

        var cursor = _useHandCursor ? _handCursorHandle : _arrowCursorHandle;
        if (cursor != 0)
        {
            SetCursor(cursor);
        }
    }

    private static bool TryGetPlacement(nint reference, out int x, out int y, out int width, out int height)
    {
        x = 0;
        y = 0;
        width = 0;
        height = 0;
        if (TryGetWorkArea(reference, out var work))
        {
            FitSquare(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top, out x, out y, out width, out height);
            return width > 0 && height > 0;
        }

        if (reference != 0 && GetClientRect(reference, out var rect))
        {
            FitSquare(0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top, out x, out y, out width, out height);
            return width > 0 && height > 0;
        }

        return false;
    }

    private static void FitSquare(int left, int top, int fullWidth, int fullHeight, out int x, out int y, out int width, out int height)
    {
        var side = Math.Min(Math.Max(0, fullWidth), Math.Max(0, fullHeight));
        x = left + ((Math.Max(0, fullWidth) - side) / 2);
        y = top + ((Math.Max(0, fullHeight) - side) / 2);
        width = side;
        height = side;
    }

    private static bool TryGetWorkArea(nint reference, out RECT work)
    {
        work = default;
        var monitor = MonitorFromWindow(reference, 2);
        if (monitor == 0)
        {
            return false;
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        work = info.Work;
        return work.Right > work.Left && work.Bottom > work.Top;
    }

    private static void ApplyColorKey(nint window) =>
        SetLayeredWindowAttributes(window, ColorKey, 0, LwaColorKey);

    private static nint FindHost()
    {
        var progman = FindWindow("Progman", "Program Manager");
        if (progman == 0)
        {
            return 0;
        }

        nint workerBehindIcons = 0;
        EnumWindows((hwnd, _) =>
        {
            var iconView = FindWindowEx(hwnd, 0, "SHELLDLL_DefView", null);
            if (iconView != 0 && ClassName(hwnd) == "WorkerW")
            {
                var next = FindWindowEx(0, hwnd, "WorkerW", null);
                if (next != 0)
                {
                    workerBehindIcons = next;
                }
            }

            return true;
        }, 0);

        return workerBehindIcons != 0 ? workerBehindIcons : progman;
    }

    private static nint FindDesktopOwner()
    {
        var progman = FindWindow("Progman", "Program Manager");
        if (progman != 0 && FindWindowEx(progman, 0, "SHELLDLL_DefView", null) != 0)
        {
            return progman;
        }

        nint owner = 0;
        EnumWindows((hwnd, _) =>
        {
            if (FindWindowEx(hwnd, 0, "SHELLDLL_DefView", null) == 0)
            {
                return true;
            }

            owner = hwnd;
            return false;
        }, 0);
        return owner != 0 ? owner : progman;
    }

    private static string ClassName(nint window)
    {
        var buffer = new StringBuilder(256);
        GetClassName(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private delegate bool EnumWindowsProcedure(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProcedure procedure, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(nint window, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint child, nint parent);

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint window, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(nint window, ref POINT point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder buffer, int max);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(nint window, int colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetCapture();

    [DllImport("user32.dll")]
    private static extern nint SetCapture(nint window);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(nint window, nint destinationDc, nint destination, ref SIZE size, nint sourceDc, ref POINT source, int colorKey, ref BlendFunction blend, uint flags);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static extern nint LoadCursor(nint instance, nint name);

    [DllImport("user32.dll")]
    private static extern nint SetCursor(nint cursor);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateDIBSection(nint dc, ref BitmapHeader header, uint usage, out nint bits, nint section, uint offset);

    private delegate nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public nint WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public nint Window;
        public nint InsertAfter;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte Operation;
        public byte Flags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
    }
}
