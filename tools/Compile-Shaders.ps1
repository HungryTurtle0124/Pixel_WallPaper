$ErrorActionPreference = 'Stop'

# Development-only shader compiler. Uses the Windows D3DCompiler DLL; no NuGet or SDK tools.
# The resulting .cso files are embedded so the renderer never compiles shaders at runtime.
if (-not ('PixelWallpaper.ShaderCompiler' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PixelWallpaper
{
    public static class ShaderCompiler
    {
        [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IBlob
        {
            [PreserveSig] IntPtr GetBufferPointer();
            [PreserveSig] UIntPtr GetBufferSize();
        }

        [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int D3DCompile(byte[] source, UIntPtr length,
            [MarshalAs(UnmanagedType.LPStr)] string name, IntPtr defines, IntPtr include,
            [MarshalAs(UnmanagedType.LPStr)] string entryPoint,
            [MarshalAs(UnmanagedType.LPStr)] string profile, uint flags, uint effectFlags,
            out IBlob code, out IBlob errors);

        public static void Compile(string sourcePath, string entryPoint, string profile, string outputPath)
        {
            byte[] source = File.ReadAllBytes(sourcePath);
            IBlob code = null;
            IBlob errors = null;
            try
            {
                // Strict syntax and optimization level 3.
                int result = D3DCompile(source, (UIntPtr)source.Length, sourcePath,
                    IntPtr.Zero, IntPtr.Zero, entryPoint, profile, (1u << 11) | (1u << 15), 0, out code, out errors);
                if (result < 0)
                {
                    string diagnostic = errors == null ? "No compiler diagnostic." :
                        Marshal.PtrToStringAnsi(errors.GetBufferPointer(), checked((int)errors.GetBufferSize().ToUInt64()));
                    throw new InvalidOperationException(sourcePath + ": " + diagnostic);
                }
                byte[] bytes = new byte[checked((int)code.GetBufferSize().ToUInt64())];
                Marshal.Copy(code.GetBufferPointer(), bytes, 0, bytes.Length);
                File.WriteAllBytes(outputPath, bytes);
            }
            finally
            {
                if (errors != null) Marshal.ReleaseComObject(errors);
                if (code != null) Marshal.ReleaseComObject(code);
            }
        }
    }
}
'@
}

$shaderDirectory = Join-Path $PSScriptRoot '../src/PixelWallpaper.Renderer/Rendering/Shaders'
foreach ($shader in @('Rain', 'Upscale')) {
    foreach ($stage in @('VS', 'PS')) {
        $profile = $stage.ToLowerInvariant() + '_5_0'
        $inputPath = Join-Path $shaderDirectory ($shader + '.hlsl')
        $outputPath = Join-Path $shaderDirectory ($shader + '.' + $stage.ToLowerInvariant() + '.cso')
        [PixelWallpaper.ShaderCompiler]::Compile($inputPath, ($stage + 'Main'), $profile, $outputPath)
        Write-Output "Compiled $shader $stage"
    }
}
