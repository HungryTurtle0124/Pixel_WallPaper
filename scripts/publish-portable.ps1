[CmdletBinding()]
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$Version = '0.10.0',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repository 'artifacts/portable' }
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$name = "PixelWallpaper-v$Version-win-x64"
$distribution = Join-Path $output $name
$archive = "$distribution.zip"
if ((Test-Path -LiteralPath $distribution) -or (Test-Path -LiteralPath $archive)) {
    throw "A release already exists at '$distribution' or '$archive'. Choose another -OutputDirectory to keep it."
}

function Publish-App([string]$Project, [string]$Destination) {
    $projectPath = "src/$Project/$Project.csproj"
    dotnet publish $projectPath -c Release -r win-x64 --self-contained true `
        --artifacts-path (Join-Path $staging 'build') --output $Destination `
        --disable-build-servers -p:PortablePublish=true -p:RestoreLockedMode=true `
        -p:PublishTrimmed=false -p:PublishSingleFile=false -p:PublishReadyToRun=false `
        -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "Publishing $Project failed (dotnet exit code $LASTEXITCODE)." }
}

function Require-Files([string]$Directory, [string[]]$Files) {
    foreach ($file in $Files) {
        $path = Join-Path $Directory $file
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
            throw "The published package is missing a required file or it is empty: $path"
        }
    }
}

function Verify-App([string]$Directory, [string]$App, [string[]]$DesktopFiles = @()) {
    $runtimeConfig = "$App.runtimeconfig.json"
    Require-Files $Directory (@("$App.exe", "$App.dll", "$App.deps.json", $runtimeConfig,
        'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'System.Private.CoreLib.dll') + $DesktopFiles)
    $config = (Get-Content -LiteralPath (Join-Path $Directory $runtimeConfig) -Raw | ConvertFrom-Json).runtimeOptions
    if ($config.includedFrameworks.name -notcontains 'Microsoft.NETCore.App') {
        throw "$App does not declare an included .NET runtime."
    }
    if ($DesktopFiles.Count -and $config.includedFrameworks.name -notcontains 'Microsoft.WindowsDesktop.App') {
        throw "$App does not declare an included Windows Desktop runtime."
    }
}

function Copy-PackageNotices([string]$Package, [string[]]$Files, [string[]]$PackageRoots) {
    foreach ($root in $PackageRoots) {
        $source = Join-Path $root $Package
        if (-not (Test-Path -LiteralPath $source -PathType Container)) { continue }
        Require-Files $source $Files
        $destination = Join-Path $folder "Notices/$($Package.Replace('/', '-'))"
        New-Item -ItemType Directory -Path $destination | Out-Null
        foreach ($file in $Files) {
            Copy-Item -LiteralPath (Join-Path $source $file) -Destination $destination
        }
        return
    }
    throw "Could not find the restored package '$Package' for redistribution notices."
}

New-Item -ItemType Directory -Path $output -Force | Out-Null
$staging = Join-Path $output ('.pixelwallpaper-stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
$folder = Join-Path $staging $name
$renderer = Join-Path $folder 'Renderer'
$tray = Join-Path $folder 'Tray'
$stagedArchive = Join-Path $staging "$name.zip"
Push-Location -LiteralPath $repository
try {
    Publish-App 'PixelWallpaper.App' $folder
    Publish-App 'PixelWallpaper.Renderer' $renderer
    Publish-App 'PixelWallpaper.Tray' $tray

    Copy-Item -LiteralPath (Join-Path $repository 'docs/PORTABLE.txt') -Destination (Join-Path $folder 'README.txt')
    Verify-App $folder 'PixelWallpaper.App' @('PresentationFramework.dll', 'PresentationNative_cor3.dll', 'vcruntime140_cor3.dll')
    Verify-App $tray 'PixelWallpaper.Tray' @('System.Windows.Forms.dll', 'vcruntime140_cor3.dll')
    Verify-App $renderer 'PixelWallpaper.Renderer'
    Require-Files $renderer @(
        'Vortice.Direct3D11.dll', 'Vortice.DXGI.dll', 'Vortice.Direct2D1.dll', 'Vortice.DirectX.dll',
        'Vortice.Mathematics.dll', 'SharpGen.Runtime.dll', 'SharpGen.Runtime.COM.dll',
        'Assets/Backgrounds/rainy-forest.png', 'Assets/Backgrounds/campfire.png', 'Assets/Backgrounds/maple-tree.png',
        'Assets/Sprites/campfire-fire.png', 'Assets/Sprites/campfire-glow.png', 'Assets/Sprites/maple-leaf-cluster.png',
        'Assets/Sprites/maple-grass.png', 'Assets/Sprites/maple-falling-leaf.png'
    )

    $assets = Get-Content -LiteralPath (Join-Path $staging 'build/obj/PixelWallpaper.App/project.assets.json') -Raw | ConvertFrom-Json
    $packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
    $config = Get-Content -LiteralPath (Join-Path $folder 'PixelWallpaper.App.runtimeconfig.json') -Raw | ConvertFrom-Json
    foreach ($framework in $config.runtimeOptions.includedFrameworks) {
        $id = $framework.name.ToLowerInvariant()
        $files = if ($id -eq 'microsoft.netcore.app') { @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT') } else { @('LICENSE') }
        Copy-PackageNotices "$id.runtime.win-x64/$($framework.version)" $files $packageRoots
    }
    $dependencies = Get-Content -LiteralPath (Join-Path $renderer 'PixelWallpaper.Renderer.deps.json') -Raw | ConvertFrom-Json
    foreach ($library in $dependencies.libraries.PSObject.Properties) {
        if ($library.Name -match '^System\.(IO\.Pipelines|Text\.(Json|Encodings\.Web))/') {
            Copy-PackageNotices $library.Value.path @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT') $packageRoots
        }
    }
    Copy-Item -LiteralPath (Join-Path $repository 'docs/THIRD-PARTY-MIT.txt') -Destination (Join-Path $folder 'Notices/Vortice-SharpGen.txt')

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($folder, $stagedArchive, [IO.Compression.CompressionLevel]::Optimal, $true)
    $folderBytes = (Get-ChildItem -LiteralPath $folder -File -Recurse -Force | Measure-Object Length -Sum).Sum
    $zipBytes = (Get-Item -LiteralPath $stagedArchive).Length
    [IO.Directory]::Move($folder, $distribution)
    [IO.File]::Move($stagedArchive, $archive)
    Write-Host ('Folder ({0:N2} MiB)  {1}' -f ($folderBytes / 1MB), $distribution)
    Write-Host ('ZIP    ({0:N2} MiB)  {1}' -f ($zipBytes / 1MB), $archive)
}
finally {
    Pop-Location
    $resolvedStage = (Resolve-Path -LiteralPath $staging).ProviderPath
    $resolvedOutput = (Resolve-Path -LiteralPath $output).ProviderPath
    if ([IO.Path]::GetDirectoryName($resolvedStage).TrimEnd('\', '/') -eq $resolvedOutput.TrimEnd('\', '/') -and
        [IO.Path]::GetFileName($resolvedStage) -match '^\.pixelwallpaper-stage-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
