using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.WIC;

namespace PixelWallpaper.Renderer.Rendering;

internal static class PngTextureLoader
{
    public static unsafe TextureResource Load(ID3D11Device device, string path, int width, int height)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Background PNG is missing. Restore Assets/Backgrounds/rainy-forest.png and rebuild.", path);
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The background must be a PNG image.");

        // WIC is apartment-based COM. Pair even S_FALSE with CoUninitialize, and ensure
        // every WIC object is released before leaving this startup-only COM scope.
        Marshal.ThrowExceptionForHR(CoInitializeEx(0, 0x00000002));
        try
        {
            using var factory = new IWICImagingFactory();
            using var decoder = factory.CreateDecoderFromFileName(path, FileAccess.Read, DecodeOptions.CacheOnLoad);
            if (decoder.FrameCount != 1)
                throw new InvalidDataException("Use one static PNG image for the background.");
            using var frame = decoder.GetFrame(0);
            frame.GetSize(out uint imageWidth, out uint imageHeight);
            // Validate before allocating a decoded pixel buffer. A fixed-size asset also
            // avoids accidental blurry resizing or a large background texture in VRAM.
            if (imageWidth != width || imageHeight != height)
                throw new InvalidDataException($"Background must be {width}x{height} pixels; received {imageWidth}x{imageHeight}. Prepare the image at the internal resolution before replacing it.");

            using var converter = factory.CreateFormatConverter();
            converter.Initialize(frame, PixelFormat.Format32bppBGRA).CheckError();
            byte[] pixels = new byte[checked(width * height * 4)];
            uint stride = checked((uint)width * 4);
            converter.CopyPixels(stride, pixels);

            var description = new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Immutable,
                BindFlags = BindFlags.ShaderResource
            };
            fixed (byte* pointer = pixels)
            {
                var texture = device.CreateTexture2D(description, new SubresourceData((nint)pointer, stride));
                try
                {
                    return new TextureResource(texture, device.CreateShaderResourceView(texture));
                }
                catch
                {
                    texture.Dispose();
                    throw;
                }
            }
            // The GPU owns a copy. Neither the decoded pixels nor the file stay retained.
        }
        finally
        {
            CoUninitialize();
        }
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, uint concurrencyModel);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}
