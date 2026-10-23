using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SphereDesk.Windows.Integration;

static class DesktopGpuPresenter
{
    const int HardwareDriver = 1;
    const uint BgraSupport = 0x20;
    const uint SdkVersion = 7;
    const int FormatBgra = 87;
    const int FlipSequential = 3;
    const int AlphaPremultiplied = 1;
    const uint RenderTargetUsage = 32;
    const int MaxVertices = 8192;

    static readonly Guid IidDxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    static readonly Guid IidFactory2 = new("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
    static readonly Guid IidTexture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    static readonly Guid IidCompositionDevice = new("C37EA93A-E7AA-450D-B16F-9746CB0407F3");

    static readonly Dictionary<byte[], GpuImage> Images = new(ReferenceEqualityComparer.Instance);
    static readonly HashSet<byte[]> Used = new(ReferenceEqualityComparer.Instance);
    static readonly List<byte[]> Stale = new();
    static readonly List<Batch> Batches = new();
    static readonly float[] Vertices = new float[MaxVertices * 8];

    static nint _device;
    static nint _context;
    static nint _swapChain;
    static nint _target;
    static nint _visual;
    static nint _composition;
    static nint _renderTarget;
    static nint _vertexBuffer;
    static nint _layout;
    static nint _vertexShader;
    static nint _pixelShader;
    static nint _discShader;
    static nint _blend;
    static nint _raster;
    static nint _sampler;
    static nint _whiteView;
    static nint _whiteTexture;
    static int _swapWidth;
    static int _swapHeight;
    static int _hitX;
    static int _hitY;
    static int _hitRadius;
    static int _regionX = int.MinValue;
    static int _regionY = int.MinValue;
    static int _regionRadius = int.MinValue;
    static int _drawSamples;
    static long _drawTicks;
    static bool _loggedPresent;
    static nint _hwnd;
    static bool _ready;

    static SetShaderDelegate? _setVertexShader;
    static SetShaderDelegate? _setPixelShader;
    static SetLayoutDelegate? _setLayout;
    static SetTopologyDelegate? _setTopology;
    static SetVertexBuffersDelegate? _setVertexBuffers;
    static SetResourcesDelegate? _setResources;
    static SetResourcesDelegate? _setSamplers;
    static SetTargetsDelegate? _setTargets;
    static SetBlendDelegate? _setBlend;
    static SetStateDelegate? _setRaster;
    static SetViewportsDelegate? _setViewports;
    static ClearDelegate? _clear;
    static MapDelegate? _map;
    static UnmapDelegate? _unmap;
    static DrawDelegate? _draw;
    static PresentDelegate? _present;
    static ResizeDelegate? _resize;

    public static bool IsReady => _ready;

    public static int LastError { get; private set; }

    public static bool Contains(int x, int y)
    {
        if (_hitRadius <= 0)
        {
            return false;
        }

        var dx = (long)x - _hitX;
        var dy = (long)y - _hitY;
        var radius = (long)_hitRadius;
        return (dx * dx) + (dy * dy) <= radius * radius;
    }

    public static bool TryPrepare()
    {
        ReleaseResources();
        LastError = 0;
        var hr = D3D11CreateDevice(0, HardwareDriver, 0, BgraSupport, 0, 0, SdkVersion, out _device, out _, out _context);
        if (hr < 0)
        {
            hr = D3D11CreateDevice(0, HardwareDriver, 0, 0, 0, 0, SdkVersion, out _device, out _, out _context);
        }

        if (hr < 0 || _device == 0 || _context == 0)
        {
            return Fail(hr);
        }

        var source = System.Text.Encoding.ASCII.GetBytes(Shader);
        var pinned = GCHandle.Alloc(source, GCHandleType.Pinned);
        nint vertexBlob = 0;
        nint pixelBlob = 0;
        nint discBlob = 0;
        nint errors = 0;
        try
        {
            hr = D3DCompile(pinned.AddrOfPinnedObject(), (nuint)source.Length, 0, 0, 0, "VS", "vs_4_0_level_9_1", 0, 0, out vertexBlob, out errors);
            Release(ref errors);
            if (hr < 0 || vertexBlob == 0)
            {
                return Fail(hr);
            }

            hr = D3DCompile(pinned.AddrOfPinnedObject(), (nuint)source.Length, 0, 0, 0, "PS", "ps_4_0_level_9_1", 0, 0, out pixelBlob, out errors);
            Release(ref errors);
            if (hr < 0 || pixelBlob == 0)
            {
                return Fail(hr);
            }

            var vertexCode = Fn<GetPointerDelegate>(vertexBlob, 3)(vertexBlob);
            var vertexLength = Fn<GetSizeDelegate>(vertexBlob, 4)(vertexBlob);
            hr = Fn<CreateShaderDelegate>(_device, 12)(_device, vertexCode, vertexLength, 0, out _vertexShader);
            if (hr < 0)
            {
                return Fail(hr);
            }

            var pixelCode = Fn<GetPointerDelegate>(pixelBlob, 3)(pixelBlob);
            var pixelLength = Fn<GetSizeDelegate>(pixelBlob, 4)(pixelBlob);
            hr = Fn<CreateShaderDelegate>(_device, 15)(_device, pixelCode, pixelLength, 0, out _pixelShader);
            if (hr < 0)
            {
                return Fail(hr);
            }

            hr = D3DCompile(pinned.AddrOfPinnedObject(), (nuint)source.Length, 0, 0, 0, "PSDisc", "ps_4_0_level_9_1", 0, 0, out discBlob, out errors);
            Release(ref errors);
            if (hr < 0 || discBlob == 0)
            {
                return Fail(hr);
            }

            var discCode = Fn<GetPointerDelegate>(discBlob, 3)(discBlob);
            var discLength = Fn<GetSizeDelegate>(discBlob, 4)(discBlob);
            hr = Fn<CreateShaderDelegate>(_device, 15)(_device, discCode, discLength, 0, out _discShader);
            if (hr < 0)
            {
                return Fail(hr);
            }

            hr = CreateLayout(vertexCode, vertexLength);
            if (hr < 0)
            {
                return Fail(hr);
            }
        }
        finally
        {
            pinned.Free();
            Release(ref vertexBlob);
            Release(ref pixelBlob);
            Release(ref discBlob);
        }

        hr = CreateStates();
        if (hr < 0)
        {
            return Fail(hr);
        }

        var white = new byte[] { 255, 255, 255, 255 };
        if (!TryUpload(white, 1, 1, out _whiteTexture, out _whiteView))
        {
            return Fail(LastError);
        }

        var buffer = new BufferDesc
        {
            ByteWidth = MaxVertices * 32,
            Usage = 2,
            BindFlags = 1,
            CpuAccessFlags = 0x10000,
        };
        hr = Fn<CreateBufferDelegate>(_device, 3)(_device, ref buffer, 0, out _vertexBuffer);
        if (hr < 0 || _vertexBuffer == 0)
        {
            return Fail(hr);
        }

        CacheContext();
        return true;
    }

    public static bool Bind(nint hwnd, int width, int height)
    {
        if (_device == 0 || hwnd == 0 || width < 1 || height < 1)
        {
            return Fail(-1);
        }

        var iid = IidDxgiDevice;
        var hr = Fn<QueryDelegate>(_device, 0)(_device, ref iid, out var dxgi);
        if (hr < 0 || dxgi == 0)
        {
            return Fail(hr);
        }

        hr = Fn<GetAdapterDelegate>(dxgi, 7)(dxgi, out var adapter);
        if (hr < 0 || adapter == 0)
        {
            Release(ref dxgi);
            return Fail(hr);
        }

        iid = IidFactory2;
        hr = Fn<QueryDelegate>(adapter, 6)(adapter, ref iid, out var factory);
        Release(ref adapter);
        if (hr < 0 || factory == 0)
        {
            Release(ref dxgi);
            return Fail(hr);
        }

        var description = new SwapChainDesc
        {
            Width = (uint)width,
            Height = (uint)height,
            Format = FormatBgra,
            SampleCount = 1,
            BufferUsage = RenderTargetUsage,
            BufferCount = 2,
            SwapEffect = FlipSequential,
            AlphaMode = AlphaPremultiplied,
        };
        hr = Fn<CreateCompositionSwapChainDelegate>(factory, 24)(factory, _device, ref description, 0, out _swapChain);
        Release(ref factory);
        if (hr < 0 || _swapChain == 0)
        {
            Release(ref dxgi);
            return Fail(hr);
        }

        _swapWidth = width;
        _swapHeight = height;
        hr = CreateRenderTarget();
        if (hr < 0)
        {
            Release(ref dxgi);
            return Fail(hr);
        }

        iid = IidCompositionDevice;
        hr = DCompositionCreateDevice(dxgi, ref iid, out _composition);
        Release(ref dxgi);
        if (hr < 0 || _composition == 0)
        {
            return Fail(hr);
        }

        hr = Fn<CreateTargetDelegate>(_composition, 6)(_composition, hwnd, 0, out _target);
        if (hr < 0 || _target == 0)
        {
            return Fail(hr);
        }

        hr = Fn<CreateVisualDelegate>(_composition, 7)(_composition, out _visual);
        if (hr < 0 || _visual == 0)
        {
            return Fail(hr);
        }

        hr = Fn<SetContentDelegate>(_visual, 15)(_visual, _swapChain);
        if (hr < 0)
        {
            return Fail(hr);
        }

        hr = Fn<SetRootDelegate>(_target, 3)(_target, _visual);
        if (hr < 0)
        {
            return Fail(hr);
        }

        hr = Fn<CommitDelegate>(_composition, 3)(_composition);
        if (hr < 0)
        {
            return Fail(hr);
        }

        _present = Fn<PresentDelegate>(_swapChain, 8);
        _resize = Fn<ResizeDelegate>(_swapChain, 13);
        _hwnd = hwnd;
        _ready = true;
        LastError = 0;
        return true;
    }

    public static bool Draw(int width, int height, IReadOnlyList<DesktopLayerHost.DesktopSprite> sprites, int ringX, int ringY, int ringRadius, int hitRadius, int selectX, int selectY, int selectRadius)
    {
        _hitX = ringX;
        _hitY = ringY;
        _hitRadius = hitRadius;
        UpdateHitRegion();
        var drawStart = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!_ready || _context == 0 || _swapChain == 0 || width < 1 || height < 1)
        {
            return false;
        }

        if (!EnsureSize(width, height))
        {
            return false;
        }

        Used.Clear();
        Batches.Clear();
        var cursor = 0;
        var discCount = 0;
        if (ringRadius > 0)
        {
            cursor = WriteQuad(cursor, width, height, ringX - ringRadius, ringY - ringRadius, ringX + ringRadius, ringY + ringRadius, 1);
            discCount = cursor / 8;
        }

        if (selectRadius > 0)
        {
            var selectStart = cursor / 8;
            cursor = WriteRing(cursor, width, height, selectX, selectY, selectRadius, 2f, 122, 162, 247, 255);
            var selectCount = (cursor / 8) - selectStart;
            if (selectCount > 0)
            {
                Batches.Add(new Batch(_whiteView, selectStart, selectCount));
            }
        }

        foreach (var sprite in sprites)
        {
            var pixels = sprite.Pixels;
            if (pixels is null || sprite.Opacity == 0 || sprite.DestinationWidth < 1 || sprite.DestinationHeight < 1 || sprite.SourceWidth < 1 || sprite.SourceHeight < 1)
            {
                continue;
            }

            if ((long)sprite.SourceWidth * sprite.SourceHeight * 4 > pixels.Length)
            {
                continue;
            }

            if (!Images.TryGetValue(pixels, out var image))
            {
                if (!TryUpload(pixels, sprite.SourceWidth, sprite.SourceHeight, out var texture, out var view))
                {
                    continue;
                }

                image = new GpuImage(texture, view);
                Images[pixels] = image;
            }

            Used.Add(pixels);
            var start = cursor / 8;
            cursor = WriteQuad(cursor, width, height, sprite.X, sprite.Y, sprite.X + sprite.DestinationWidth, sprite.Y + sprite.DestinationHeight, sprite.Opacity / 255f);
            var count = (cursor / 8) - start;
            if (count > 0)
            {
                Batches.Add(new Batch(image.View, start, count));
            }
        }

        ReleaseStaleImages();
        if (_map is null || _unmap is null || _draw is null || _clear is null || _present is null)
        {
            return false;
        }

        var mappedHr = _map(_context, _vertexBuffer, 0, 4, 0, out var mapped);
        if (mappedHr < 0 || mapped.Data == 0)
        {
            LastError = mappedHr;
            return false;
        }

        Marshal.Copy(Vertices, 0, mapped.Data, cursor);
        _unmap(_context, _vertexBuffer, 0);
        BindPipeline(width, height);
        var black = new Color4();
        _clear(_context, _renderTarget, ref black);
        if (discCount > 0 && _discShader != 0)
        {
            _setPixelShader!(_context, _discShader, 0, 0);
            _draw(_context, (uint)discCount, 0);
            _setPixelShader!(_context, _pixelShader, 0, 0);
        }

        foreach (var batch in Batches)
        {
            var view = batch.View;
            _setResources!(_context, 0, 1, ref view);
            _draw(_context, (uint)batch.Count, (uint)batch.Start);
        }

        var presentHr = _present(_swapChain, 0, 0);
        if (presentHr < 0)
        {
            presentHr = _present(_swapChain, 1, 0);
        }

        if (!_loggedPresent)
        {
            _loggedPresent = true;
            AppLog.Write("DesktopGpuPresent", presentHr);
        }

        if (presentHr < 0)
        {
            LastError = presentHr;
            return false;
        }

        _drawTicks += System.Diagnostics.Stopwatch.GetTimestamp() - drawStart;
        _drawSamples++;
        if (_drawSamples == 60)
        {
            var milliseconds = _drawTicks / (double)System.Diagnostics.Stopwatch.Frequency * 1000 / 60;
            AppLog.Write("DesktopGpuMs", (int)Math.Round(milliseconds));
        }

        return true;
    }

    static void UpdateHitRegion()
    {
        if (_hwnd == 0 || _hitRadius <= 0 || (_hitX == _regionX && _hitY == _regionY && _hitRadius == _regionRadius))
        {
            return;
        }

        var region = CreateEllipticRgn(_hitX - _hitRadius, _hitY - _hitRadius, _hitX + _hitRadius + 1, _hitY + _hitRadius + 1);
        if (region == 0)
        {
            return;
        }

        if (SetWindowRgn(_hwnd, region, 1) == 0)
        {
            DeleteObject(region);
            return;
        }

        _regionX = _hitX;
        _regionY = _hitY;
        _regionRadius = _hitRadius;
    }

    public static void Dispose()
    {
        ReleaseResources();
    }

    static bool EnsureSize(int width, int height)
    {
        if (width == _swapWidth && height == _swapHeight && _renderTarget != 0)
        {
            return true;
        }

        Release(ref _renderTarget);
        if (_resize is null)
        {
            return false;
        }

        var hr = _resize(_swapChain, 0, (uint)width, (uint)height, FormatBgra, 0);
        if (hr < 0)
        {
            LastError = hr;
            return false;
        }

        _swapWidth = width;
        _swapHeight = height;
        hr = CreateRenderTarget();
        if (hr < 0)
        {
            LastError = hr;
            return false;
        }

        return true;
    }

    static int CreateRenderTarget()
    {
        var iid = IidTexture2D;
        var hr = Fn<GetBufferDelegate>(_swapChain, 9)(_swapChain, 0, ref iid, out var buffer);
        if (hr < 0 || buffer == 0)
        {
            return hr;
        }

        hr = Fn<CreateViewDelegate>(_device, 9)(_device, buffer, 0, out _renderTarget);
        Release(ref buffer);
        return hr;
    }

    static void BindPipeline(int width, int height)
    {
        _setVertexShader!(_context, _vertexShader, 0, 0);
        _setPixelShader!(_context, _pixelShader, 0, 0);
        _setLayout!(_context, _layout);
        _setTopology!(_context, 4);
        var buffer = _vertexBuffer;
        uint stride = 32;
        uint offset = 0;
        _setVertexBuffers!(_context, 0, 1, ref buffer, ref stride, ref offset);
        var sampler = _sampler;
        _setSamplers!(_context, 0, 1, ref sampler);
        var target = _renderTarget;
        _setTargets!(_context, 1, ref target, 0);
        var factor = new Color4 { R = 1, G = 1, B = 1, A = 1 };
        _setBlend!(_context, _blend, ref factor, uint.MaxValue);
        _setRaster!(_context, _raster);
        var viewport = new Viewport { Width = width, Height = height, MaxDepth = 1 };
        _setViewports!(_context, 1, ref viewport);
    }

    static void CacheContext()
    {
        _setVertexShader = Fn<SetShaderDelegate>(_context, 11);
        _setPixelShader = Fn<SetShaderDelegate>(_context, 9);
        _setLayout = Fn<SetLayoutDelegate>(_context, 17);
        _setVertexBuffers = Fn<SetVertexBuffersDelegate>(_context, 18);
        _setTopology = Fn<SetTopologyDelegate>(_context, 24);
        _setResources = Fn<SetResourcesDelegate>(_context, 8);
        _setSamplers = Fn<SetResourcesDelegate>(_context, 10);
        _setTargets = Fn<SetTargetsDelegate>(_context, 33);
        _setBlend = Fn<SetBlendDelegate>(_context, 35);
        _setRaster = Fn<SetStateDelegate>(_context, 43);
        _setViewports = Fn<SetViewportsDelegate>(_context, 44);
        _clear = Fn<ClearDelegate>(_context, 50);
        _map = Fn<MapDelegate>(_context, 14);
        _unmap = Fn<UnmapDelegate>(_context, 15);
        _draw = Fn<DrawDelegate>(_context, 13);
    }

    static int CreateLayout(nint bytecode, nuint length)
    {
        var position = Marshal.StringToHGlobalAnsi("POSITION");
        var texcoord = Marshal.StringToHGlobalAnsi("TEXCOORD");
        var color = Marshal.StringToHGlobalAnsi("COLOR");
        var elements = new InputElement[3];
        elements[0] = new InputElement { SemanticName = position, Format = 16, AlignedByteOffset = 0 };
        elements[1] = new InputElement { SemanticName = texcoord, Format = 16, AlignedByteOffset = 8 };
        elements[2] = new InputElement { SemanticName = color, Format = 2, AlignedByteOffset = 16 };
        var pinned = GCHandle.Alloc(elements, GCHandleType.Pinned);
        try
        {
            return Fn<CreateLayoutDelegate>(_device, 11)(_device, pinned.AddrOfPinnedObject(), 3, bytecode, length, out _layout);
        }
        finally
        {
            pinned.Free();
            Marshal.FreeHGlobal(position);
            Marshal.FreeHGlobal(texcoord);
            Marshal.FreeHGlobal(color);
        }
    }

    static int CreateStates()
    {
        var blend = new BlendDesc { Target0 = new TargetBlend { BlendEnable = 1, Src = 2, Dest = 6, Op = 1, SrcAlpha = 2, DestAlpha = 6, OpAlpha = 1, Mask = 15 } };
        var hr = Fn<CreateBlendDelegate>(_device, 20)(_device, ref blend, out _blend);
        if (hr < 0)
        {
            return hr;
        }

        var raster = new RasterizerDesc { FillMode = 3, CullMode = 1, DepthClipEnable = 1 };
        hr = Fn<CreateRasterDelegate>(_device, 22)(_device, ref raster, out _raster);
        if (hr < 0)
        {
            return hr;
        }

        var sampler = new SamplerDesc
        {
            Filter = 0x15,
            AddressU = 3,
            AddressV = 3,
            AddressW = 3,
            Comparison = 1,
            MaxLod = float.MaxValue,
        };
        return Fn<CreateSamplerDelegate>(_device, 23)(_device, ref sampler, out _sampler);
    }

    static bool TryUpload(byte[] pixels, int width, int height, out nint texture, out nint view)
    {
        texture = 0;
        view = 0;
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        var data = new SubresourceData { SysMem = pin.AddrOfPinnedObject(), Pitch = (uint)(width * 4) };
        var dataPointer = Marshal.AllocHGlobal(Marshal.SizeOf<SubresourceData>());
        try
        {
            Marshal.StructureToPtr(data, dataPointer, false);
            var description = new TextureDesc
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = FormatBgra,
                SampleCount = 1,
                BindFlags = 8,
            };
            var hr = Fn<CreateTextureDelegate>(_device, 5)(_device, ref description, dataPointer, out texture);
            if (hr < 0 || texture == 0)
            {
                LastError = hr;
                return false;
            }

            var srv = new SrvDesc { Format = FormatBgra, ViewDimension = 4, MipLevels = 1 };
            hr = Fn<CreateSrvDelegate>(_device, 7)(_device, texture, ref srv, out view);
            if (hr < 0 || view == 0)
            {
                LastError = hr;
                Release(ref texture);
                return false;
            }

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(dataPointer);
            pin.Free();
        }
    }

    static int WriteRing(int cursor, int width, int height, int centerX, int centerY, int radius, float thickness, byte red, byte green, byte blue, byte alpha)
    {
        if (radius <= 0 || alpha == 0)
        {
            return cursor;
        }

        var scale = alpha / 255f;
        var colorR = red / 255f * scale;
        var colorG = green / 255f * scale;
        var colorB = blue / 255f * scale;
        var outer = radius;
        var inner = Math.Max(0, radius - thickness);
        const int segments = 96;
        for (var index = 0; index < segments; index++)
        {
            var start = index * MathF.PI * 2f / segments;
            var end = (index + 1) * MathF.PI * 2f / segments;
            var c0 = MathF.Cos(start);
            var s0 = MathF.Sin(start);
            var c1 = MathF.Cos(end);
            var s1 = MathF.Sin(end);
            cursor = Vertex(cursor, width, height, centerX + (c0 * outer), centerY + (s0 * outer), 0, 0, colorR, colorG, colorB, scale);
            cursor = Vertex(cursor, width, height, centerX + (c1 * outer), centerY + (s1 * outer), 1, 0, colorR, colorG, colorB, scale);
            cursor = Vertex(cursor, width, height, centerX + (c0 * inner), centerY + (s0 * inner), 0, 1, colorR, colorG, colorB, scale);
            cursor = Vertex(cursor, width, height, centerX + (c1 * outer), centerY + (s1 * outer), 1, 0, colorR, colorG, colorB, scale);
            cursor = Vertex(cursor, width, height, centerX + (c1 * inner), centerY + (s1 * inner), 1, 1, colorR, colorG, colorB, scale);
            cursor = Vertex(cursor, width, height, centerX + (c0 * inner), centerY + (s0 * inner), 0, 1, colorR, colorG, colorB, scale);
        }

        return cursor;
    }

    static int WriteQuad(int cursor, int width, int height, float left, float top, float right, float bottom, float opacity)
    {
        cursor = Vertex(cursor, width, height, left, top, 0, 0, opacity, opacity, opacity, opacity);
        cursor = Vertex(cursor, width, height, right, top, 1, 0, opacity, opacity, opacity, opacity);
        cursor = Vertex(cursor, width, height, left, bottom, 0, 1, opacity, opacity, opacity, opacity);
        cursor = Vertex(cursor, width, height, right, top, 1, 0, opacity, opacity, opacity, opacity);
        cursor = Vertex(cursor, width, height, right, bottom, 1, 1, opacity, opacity, opacity, opacity);
        cursor = Vertex(cursor, width, height, left, bottom, 0, 1, opacity, opacity, opacity, opacity);
        return cursor;
    }

    static int Vertex(int cursor, int width, int height, float x, float y, float u, float v, float r, float g, float b, float a)
    {
        if (cursor + 8 > Vertices.Length || width < 1 || height < 1)
        {
            return cursor;
        }

        Vertices[cursor++] = (x / width * 2f) - 1f;
        Vertices[cursor++] = 1f - (y / height * 2f);
        Vertices[cursor++] = u;
        Vertices[cursor++] = v;
        Vertices[cursor++] = r;
        Vertices[cursor++] = g;
        Vertices[cursor++] = b;
        Vertices[cursor++] = a;
        return cursor;
    }

    static void ReleaseStaleImages()
    {
        Stale.Clear();
        foreach (var pair in Images)
        {
            if (!Used.Contains(pair.Key))
            {
                Stale.Add(pair.Key);
            }
        }

        foreach (var key in Stale)
        {
            if (!Images.Remove(key, out var image))
            {
                continue;
            }

            var view = image.View;
            var texture = image.Texture;
            Release(ref view);
            Release(ref texture);
        }
    }

    static bool Fail(int hr)
    {
        LastError = hr == 0 ? -1 : hr;
        ReleaseResources();
        return false;
    }

    static void ReleaseResources()
    {
        _ready = false;
        Release(ref _renderTarget);
        foreach (var image in Images.Values)
        {
            var view = image.View;
            var texture = image.Texture;
            Release(ref view);
            Release(ref texture);
        }

        Images.Clear();
        Used.Clear();
        Release(ref _whiteView);
        Release(ref _whiteTexture);
        Release(ref _blend);
        Release(ref _raster);
        Release(ref _sampler);
        Release(ref _layout);
        Release(ref _vertexShader);
        Release(ref _pixelShader);
        Release(ref _discShader);
        Release(ref _vertexBuffer);
        Release(ref _visual);
        Release(ref _target);
        Release(ref _composition);
        Release(ref _swapChain);
        Release(ref _context);
        Release(ref _device);
        _swapWidth = 0;
        _swapHeight = 0;
        _hwnd = 0;
        _regionRadius = int.MinValue;
        _present = null;
        _resize = null;
    }

    static void Release(ref nint unknown)
    {
        if (unknown == 0)
        {
            return;
        }

        Fn<ReleaseDelegate>(unknown, 2)(unknown);
        unknown = 0;
    }

    static T Fn<T>(nint unknown, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(unknown), slot * IntPtr.Size));

    const string Shader = """
        Texture2D tex0 : register(t0);
        SamplerState samp0 : register(s0);
        struct Vin { float2 pos : POSITION; float2 uv : TEXCOORD; float4 color : COLOR; };
        struct Vout { float4 pos : SV_Position; float2 uv : TEXCOORD; float4 color : COLOR; };
        Vout VS(Vin input) { Vout output; output.pos = float4(input.pos, 0, 1); output.uv = input.uv; output.color = input.color; return output; }
        float4 PS(Vout input) : SV_Target { return tex0.Sample(samp0, input.uv) * input.color; }
        float4 PSDisc(Vout input) : SV_Target {
            float2 p = input.uv * 2.0 - 1.0;
            p.y = -p.y;
            float r2 = dot(p, p);
            float edge = 1.0 - smoothstep(0.90, 1.0, r2);
            float z = sqrt(max(0.0, 1.0 - min(r2, 1.0)));
            float3 n = normalize(float3(p.x, p.y, z));
            float3 light = normalize(float3(-0.45, 0.55, 0.70));
            float shade = 0.18 + 0.82 * saturate(dot(n, light));
            float3 color = lerp(float3(18.0, 20.0, 26.0) / 255.0, float3(176.0, 198.0, 230.0) / 255.0, shade);
            float alpha = 0.42 * edge * (0.35 + 0.65 * shade);
            return float4(color * alpha, alpha);
        }
        """;

    readonly struct GpuImage(nint texture, nint view)
    {
        public nint Texture { get; } = texture;
        public nint View { get; } = view;
    }

    readonly struct Batch(nint view, int start, int count)
    {
        public nint View { get; } = view;
        public int Start { get; } = start;
        public int Count { get; } = count;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SwapChainDesc
    {
        public uint Width;
        public uint Height;
        public int Format;
        public int Stereo;
        public uint SampleCount;
        public uint SampleQuality;
        public uint BufferUsage;
        public uint BufferCount;
        public int Scaling;
        public int SwapEffect;
        public int AlphaMode;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TextureDesc
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public int Format;
        public uint SampleCount;
        public uint SampleQuality;
        public int Usage;
        public uint BindFlags;
        public uint CpuAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SubresourceData
    {
        public nint SysMem;
        public uint Pitch;
        public uint SlicePitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SrvDesc
    {
        public int Format;
        public int ViewDimension;
        public uint MostDetailedMip;
        public uint MipLevels;
        public uint Pad0;
        public uint Pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BufferDesc
    {
        public uint ByteWidth;
        public int Usage;
        public uint BindFlags;
        public uint CpuAccessFlags;
        public uint MiscFlags;
        public uint StructureByteStride;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct InputElement
    {
        public nint SemanticName;
        public uint SemanticIndex;
        public int Format;
        public uint InputSlot;
        public uint AlignedByteOffset;
        public int SlotClass;
        public uint InstanceDataStepRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TargetBlend
    {
        public int BlendEnable;
        public int Src;
        public int Dest;
        public int Op;
        public int SrcAlpha;
        public int DestAlpha;
        public int OpAlpha;
        public byte Mask;
        public byte Pad0;
        public byte Pad1;
        public byte Pad2;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BlendDesc
    {
        public int AlphaToCoverageEnable;
        public int IndependentBlendEnable;
        public TargetBlend Target0;
        public TargetBlend Target1;
        public TargetBlend Target2;
        public TargetBlend Target3;
        public TargetBlend Target4;
        public TargetBlend Target5;
        public TargetBlend Target6;
        public TargetBlend Target7;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RasterizerDesc
    {
        public int FillMode;
        public int CullMode;
        public int FrontCounterClockwise;
        public int DepthBias;
        public float DepthBiasClamp;
        public float SlopeScaledDepthBias;
        public int DepthClipEnable;
        public int ScissorEnable;
        public int MultisampleEnable;
        public int AntialiasedLineEnable;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SamplerDesc
    {
        public int Filter;
        public int AddressU;
        public int AddressV;
        public int AddressW;
        public float MipBias;
        public uint MaxAnisotropy;
        public int Comparison;
        public float Border0;
        public float Border1;
        public float Border2;
        public float Border3;
        public float MinLod;
        public float MaxLod;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Viewport
    {
        public float TopLeftX;
        public float TopLeftY;
        public float Width;
        public float Height;
        public float MinDepth;
        public float MaxDepth;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Color4
    {
        public float R;
        public float G;
        public float B;
        public float A;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MappedSubresource
    {
        public nint Data;
        public uint RowPitch;
        public uint DepthPitch;
    }

    delegate uint ReleaseDelegate(nint self);
    delegate int QueryDelegate(nint self, ref Guid iid, out nint result);
    delegate int GetAdapterDelegate(nint self, out nint adapter);
    delegate int CreateCompositionSwapChainDelegate(nint self, nint device, ref SwapChainDesc description, nint restrictToOutput, out nint swapChain);
    delegate int GetBufferDelegate(nint self, uint index, ref Guid iid, out nint surface);
    delegate int PresentDelegate(nint self, uint syncInterval, uint flags);
    delegate int ResizeDelegate(nint self, uint bufferCount, uint width, uint height, int format, uint flags);
    delegate int CreateTargetDelegate(nint self, nint hwnd, int topmost, out nint target);
    delegate int CreateVisualDelegate(nint self, out nint visual);
    delegate int SetContentDelegate(nint self, nint content);
    delegate int SetRootDelegate(nint self, nint visual);
    delegate int CommitDelegate(nint self);
    delegate nint GetPointerDelegate(nint self);
    delegate nuint GetSizeDelegate(nint self);
    delegate int CreateShaderDelegate(nint self, nint bytecode, nuint length, nint linkage, out nint shader);
    delegate int CreateLayoutDelegate(nint self, nint elements, uint count, nint bytecode, nuint length, out nint layout);
    delegate int CreateBufferDelegate(nint self, ref BufferDesc description, nint initialData, out nint buffer);
    delegate int CreateTextureDelegate(nint self, ref TextureDesc description, nint initialData, out nint texture);
    delegate int CreateSrvDelegate(nint self, nint resource, ref SrvDesc description, out nint view);
    delegate int CreateViewDelegate(nint self, nint resource, nint description, out nint view);
    delegate int CreateBlendDelegate(nint self, ref BlendDesc description, out nint state);
    delegate int CreateRasterDelegate(nint self, ref RasterizerDesc description, out nint state);
    delegate int CreateSamplerDelegate(nint self, ref SamplerDesc description, out nint state);
    delegate void SetShaderDelegate(nint self, nint shader, nint instances, uint count);
    delegate void SetLayoutDelegate(nint self, nint layout);
    delegate void SetTopologyDelegate(nint self, int topology);
    delegate void SetVertexBuffersDelegate(nint self, uint start, uint count, ref nint buffers, ref uint stride, ref uint offset);
    delegate void SetResourcesDelegate(nint self, uint start, uint count, ref nint resources);
    delegate void SetTargetsDelegate(nint self, uint count, ref nint views, nint depth);
    delegate void SetBlendDelegate(nint self, nint state, ref Color4 factor, uint mask);
    delegate void SetStateDelegate(nint self, nint state);
    delegate void SetViewportsDelegate(nint self, uint count, ref Viewport viewport);
    delegate void ClearDelegate(nint self, nint view, ref Color4 color);
    delegate int MapDelegate(nint self, nint resource, uint subresource, int mapType, uint mapFlags, out MappedSubresource mapped);
    delegate void UnmapDelegate(nint self, nint resource, uint subresource);
    delegate void DrawDelegate(nint self, uint vertexCount, uint start);

    [DllImport("d3d11.dll")]
    static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint featureLevels, uint featureLevelCount, uint sdkVersion, out nint device, out int featureLevel, out nint context);

    [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)]
    static extern int D3DCompile(nint source, nuint sourceSize, nint name, nint defines, nint include, string entry, string target, uint flags1, uint flags2, out nint code, out nint errors);

    [DllImport("dcomp.dll")]
    static extern int DCompositionCreateDevice(nint dxgiDevice, ref Guid iid, out nint device);

    [DllImport("gdi32.dll")]
    static extern nint CreateEllipticRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(nint obj);

    [DllImport("user32.dll")]
    static extern int SetWindowRgn(nint window, nint region, int redraw);
}
