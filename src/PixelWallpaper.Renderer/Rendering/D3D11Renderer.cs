using System.Reflection;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PixelWallpaper.Renderer.Scenes;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace PixelWallpaper.Renderer.Rendering;

internal sealed class D3D11Renderer : IDisposable
{
    public const int InternalWidth = 480;
    public const int InternalHeight = 270;
    private const SwapChainFlags ChainFlags = SwapChainFlags.FrameLatencyWaitableObject;
    private static readonly Color4 Background = new(8 / 255f, 12 / 255f, 20 / 255f, 1);

    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _context = null!;
    private IDXGISwapChain2 _swapChain = null!;
    private SafeWaitHandle? _frameLatency;
    private ID3D11RenderTargetView? _backBufferView;
    private ID3D11Texture2D? _sceneTexture;
    private ID3D11RenderTargetView? _sceneTarget;
    private ID3D11ShaderResourceView? _sceneView;
    private ID3D11Buffer? _vertices;
    private ID3D11VertexShader? _rainVertexShader;
    private ID3D11PixelShader? _rainPixelShader;
    private ID3D11VertexShader? _upscaleVertexShader;
    private ID3D11PixelShader? _upscalePixelShader;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11SamplerState? _pointSampler;
    private ID3D11RasterizerState? _rasterizer;
    // Created only for a scene using sprites; Rainy Forest does not pay this resource cost.
    private ID3D11Buffer? _spriteVertices;
    private ID3D11VertexShader? _spriteVertexShader;
    private ID3D11PixelShader? _spritePixelShader;
    private ID3D11InputLayout? _spriteLayout;
    private ID3D11BlendState? _alphaBlend;
    private readonly int _vertexCapacity;
    private int _width;
    private int _height;
    private bool _disposed;

    public nint FrameLatencyHandle => _frameLatency!.DangerousGetHandle();

    public D3D11Renderer(nint window, int width, int height, int vertexCapacity = 1024)
    {
        _vertexCapacity = vertexCapacity;
        try
        {
            DeviceCreationFlags flags = DeviceCreationFlags.BgraSupport;
#if DEBUG
            flags |= DeviceCreationFlags.Debug;
#endif
            var result = D3D11.D3D11CreateDevice(nint.Zero, DriverType.Hardware, flags,
                [FeatureLevel.Level_11_0], out _device, out _context);
#if DEBUG
            // Graphics Tools is optional. Fall back only when the debug layer is missing.
            if (result.Code == unchecked((int)0x887A002D))
            {
                _context?.Dispose();
                _device?.Dispose();
                result = D3D11.D3D11CreateDevice(nint.Zero, DriverType.Hardware,
                    DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_0], out _device, out _context);
            }
#endif
            result.CheckError();

            using var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);
            var chainDescription = new SwapChainDescription1
            {
                Width = (uint)width,
                Height = (uint)height,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                BufferCount = 2,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipDiscard,
                AlphaMode = AlphaMode.Ignore,
                Flags = ChainFlags
            };
            using (var chain = factory.CreateSwapChainForHwnd(_device, window, chainDescription))
                _swapChain = chain.QueryInterface<IDXGISwapChain2>();
            factory.MakeWindowAssociation(window, WindowAssociationFlags.IgnoreAltEnter).CheckError();
            _swapChain.MaximumFrameLatency = 1;
            _frameLatency = new SafeWaitHandle(_swapChain.FrameLatencyWaitableObject, ownsHandle: true);
            if (_frameLatency.IsInvalid)
                throw new InvalidOperationException("DXGI did not supply a frame-latency wait handle.");

            var sceneDescription = new Texture2DDescription
            {
                Width = InternalWidth,
                Height = InternalHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource
            };
            _sceneTexture = _device.CreateTexture2D(sceneDescription);
            _sceneTarget = _device.CreateRenderTargetView(_sceneTexture);
            _sceneView = _device.CreateShaderResourceView(_sceneTexture);
            _vertices = _device.CreateBuffer((uint)(vertexCapacity * Marshal.SizeOf<ColoredVertex>()),
                BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);

            byte[] rainVertexCode = LoadShader("Rain.vs.cso");
            _rainVertexShader = _device.CreateVertexShader(rainVertexCode);
            _rainPixelShader = _device.CreatePixelShader(LoadShader("Rain.ps.cso"));
            _upscaleVertexShader = _device.CreateVertexShader(LoadShader("Upscale.vs.cso"));
            _upscalePixelShader = _device.CreatePixelShader(LoadShader("Upscale.ps.cso"));
            _inputLayout = _device.CreateInputLayout(
            [
                new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
                new InputElementDescription("COLOR", 0, Format.R8G8B8A8_UNorm, 8, 0)
            ], rainVertexCode);
            _pointSampler = _device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.MinMagMipPoint,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                MaxAnisotropy = 1,
                ComparisonFunc = ComparisonFunction.Never,
                MaxLOD = float.MaxValue
            });
            _rasterizer = _device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid));
            CreateBackBufferView(width, height);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static byte[] LoadShader(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            $"PixelWallpaper.Renderer.Rendering.Shaders.{name}")
            ?? throw new InvalidOperationException($"Embedded shader {name} is missing. Run tools/Compile-Shaders.ps1, then rebuild.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0 || (_width == width && _height == height))
            return;

        // Release every reference to the old back buffer, including context bindings.
        // Size messages are coalesced by the outer loop rather than resizing per message.
        _context.ClearState();
        _backBufferView?.Dispose();
        _backBufferView = null;
        _swapChain.ResizeBuffers(2, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, ChainFlags).CheckError();
        CreateBackBufferView(width, height);
    }

    private void CreateBackBufferView(int width, int height)
    {
        using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _backBufferView = _device.CreateRenderTargetView(backBuffer);
        _width = width;
        _height = height;
    }

    public TextureResource LoadBackgroundTexture(string imagePath) =>
        PngTextureLoader.Load(_device, imagePath, InternalWidth, InternalHeight);

    public TextureResource LoadSpriteTexture(string imagePath, int width, int height)
    {
        if (_spriteVertices is null)
        {
            byte[] code = LoadShader("Sprite.vs.cso");
            _spriteVertexShader = _device.CreateVertexShader(code);
            _spritePixelShader = _device.CreatePixelShader(LoadShader("Sprite.ps.cso"));
            _spriteLayout = _device.CreateInputLayout(
            [
                new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 8, 0),
                new InputElementDescription("COLOR", 0, Format.R8G8B8A8_UNorm, 16, 0)
            ], code);
            _alphaBlend = _device.CreateBlendState(BlendDescription.NonPremultiplied);
            _spriteVertices = _device.CreateBuffer((uint)(6 * Marshal.SizeOf<SpriteVertex>()),
                BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        }
        // WIC converts to straight BGRA, paired with non-premultiplied alpha blending.
        return PngTextureLoader.Load(_device, imagePath, width, height);
    }

    public unsafe void DrawSprite(TextureResource texture, int x, int y, int width, int height,
        float u0 = 0, float u1 = 1, byte opacity = 255)
    {
        if (_spriteVertices is null) throw new InvalidOperationException("Load a sprite texture before drawing.");
        uint color = 0x00FFFFFFu | ((uint)opacity << 24);
        Vector2 tl = new(x * (2f / InternalWidth) - 1, 1 - y * (2f / InternalHeight));
        Vector2 br = new((x + width) * (2f / InternalWidth) - 1, 1 - (y + height) * (2f / InternalHeight));
        Span<SpriteVertex> quad = stackalloc SpriteVertex[6];
        quad[0] = new(tl, new(u0, 0), color);
        quad[1] = new(new(br.X, tl.Y), new(u1, 0), color);
        quad[2] = new(new(tl.X, br.Y), new(u0, 1), color);
        quad[3] = quad[2]; quad[4] = quad[1]; quad[5] = new(br, new(u1, 1), color);
        var mapped = _context.Map(_spriteVertices, MapMode.WriteDiscard);
        try { quad.CopyTo(new Span<SpriteVertex>((void*)mapped.DataPointer, 6)); }
        finally { _context.Unmap(_spriteVertices, 0); }
        _context.OMSetBlendState(_alphaBlend);
        _context.IASetInputLayout(_spriteLayout);
        _context.IASetVertexBuffer(0, _spriteVertices, (uint)sizeof(SpriteVertex));
        _context.VSSetShader(_spriteVertexShader);
        _context.PSSetShader(_spritePixelShader);
        _context.PSSetShaderResource(0, texture.View);
        _context.PSSetSampler(0, _pointSampler);
        _context.Draw(6, 0);
    }

    public void Render(IScene scene)
    {
        // The engine owns the frame and presentation. The scene supplies ordered draws.
        // The Vortice helper accepts native null bindings despite its non-null annotation.
        _context.PSSetShaderResource(0, null!);
        _context.OMSetRenderTargets(_sceneTarget!);
        _context.ClearRenderTargetView(_sceneTarget!, Background);
        _context.RSSetState(_rasterizer);
        _context.RSSetViewport(0, 0, InternalWidth, InternalHeight);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        scene.Render(this);
        PresentScene();
    }

    public void DrawBackground(TextureResource texture)
    {
        _context.OMSetBlendState(null);
        // Reuse the same point-sampled fullscreen-triangle pipeline as the upscale pass.
        _context.IASetInputLayout(null);
        _context.VSSetShader(_upscaleVertexShader);
        _context.PSSetShader(_upscalePixelShader);
        _context.PSSetShaderResource(0, texture.View);
        _context.PSSetSampler(0, _pointSampler);
        _context.Draw(3, 0);
    }

    public unsafe void DrawParticles(ReadOnlySpan<ColoredVertex> vertices, bool alphaBlend = false)
    {
        if (vertices.Length > _vertexCapacity)
            throw new ArgumentOutOfRangeException(nameof(vertices));

        var mapped = _context.Map(_vertices!, MapMode.WriteDiscard);
        try
        {
            vertices.CopyTo(new Span<ColoredVertex>((void*)mapped.DataPointer, vertices.Length));
        }
        finally
        {
            _context.Unmap(_vertices!, 0);
        }

        _context.OMSetBlendState(alphaBlend ? _alphaBlend : null);
        _context.PSSetShaderResource(0, null!);
        _context.IASetInputLayout(_inputLayout);
        _context.IASetVertexBuffer(0, _vertices!, (uint)sizeof(ColoredVertex));
        _context.VSSetShader(_rainVertexShader);
        _context.PSSetShader(_rainPixelShader);
        _context.Draw((uint)vertices.Length, 0);
    }

    private void PresentScene()
    {
        _context.OMSetBlendState(null);
        _context.OMSetRenderTargets(_backBufferView!);
        _context.ClearRenderTargetView(_backBufferView!, Background);

        // Prefer integer enlargement; center with bars instead of stretching the aspect ratio.
        float scale = Math.Min(_width / (float)InternalWidth, _height / (float)InternalHeight);
        if (scale >= 1)
            scale = MathF.Floor(scale);
        float outputWidth = MathF.Max(1, MathF.Floor(InternalWidth * scale));
        float outputHeight = MathF.Max(1, MathF.Floor(InternalHeight * scale));
        _context.RSSetViewport(MathF.Floor((_width - outputWidth) / 2), MathF.Floor((_height - outputHeight) / 2),
            outputWidth, outputHeight);
        _context.IASetInputLayout(null);
        _context.VSSetShader(_upscaleVertexShader);
        _context.PSSetShader(_upscalePixelShader);
        _context.PSSetShaderResource(0, _sceneView!);
        _context.PSSetSampler(0, _pointSampler);
        _context.Draw(3, 0);

        // VSync prevents tearing; FramePacer supplies the selected FPS upper limit.
        _swapChain.Present(1, PresentFlags.None).CheckError();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _context?.ClearState();
        _backBufferView?.Dispose();
        _sceneView?.Dispose();
        _sceneTarget?.Dispose();
        _sceneTexture?.Dispose();
        _vertices?.Dispose();
        _spriteVertices?.Dispose();
        _spriteLayout?.Dispose();
        _spriteVertexShader?.Dispose();
        _spritePixelShader?.Dispose();
        _alphaBlend?.Dispose();
        _inputLayout?.Dispose();
        _rainVertexShader?.Dispose();
        _rainPixelShader?.Dispose();
        _upscaleVertexShader?.Dispose();
        _upscalePixelShader?.Dispose();
        _pointSampler?.Dispose();
        _rasterizer?.Dispose();
        _frameLatency?.Dispose();
        _swapChain?.Dispose();
        // Submit deferred destruction once at shutdown, never force a GPU wait per frame.
        _context?.Flush();
        _context?.Dispose();
        _device?.Dispose();
    }
}
