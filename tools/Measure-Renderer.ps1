param(
    [ValidateRange(1, 60)][int]$Seconds = 15,
    [int]$ProcessId = 0,
    [switch]$IncludeGpu
)

$ErrorActionPreference = 'Stop'
if ($ProcessId -eq 0) {
    $candidates = @(Get-Process -Name 'PixelWallpaper.Renderer' -ErrorAction SilentlyContinue)
    if ($candidates.Count -ne 1) {
        throw 'Start one renderer, or specify its PID with -ProcessId.'
    }
    $ProcessId = $candidates[0].Id
}

$rendererProcess = Get-Process -Id $ProcessId
$startCpu = $rendererProcess.TotalProcessorTime.TotalSeconds
$startMemory = $rendererProcess.WorkingSet64
$startHandles = $rendererProcess.HandleCount
$watch = [Diagnostics.Stopwatch]::StartNew()
$gpuSamples = [Collections.Generic.List[double]]::new()
$gpuMemory = $null
$gpuStatus = 'Not requested'
if ($IncludeGpu) {
    try {
        for ($sample = 0; $sample -lt $Seconds; $sample++) {
            $engines = @(Get-CimInstance -ClassName Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine -ErrorAction Stop |
                Where-Object { $_.Name -match "^pid_${ProcessId}_.*engtype_3D" })
            if ($engines.Count -eq 0) { throw 'No process 3D-engine counters were exposed by the driver.' }
            $gpuSamples.Add([double](($engines | Measure-Object UtilizationPercentage -Maximum).Maximum))
            Start-Sleep -Seconds 1
        }
        $gpuMemory = @(Get-CimInstance -ClassName Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory -ErrorAction Stop |
            Where-Object { $_.Name -match "^pid_${ProcessId}_" })
        $gpuStatus = 'Driver/WMI counters; busiest process 3D engine per sample; excludes DWM work'
    }
    catch {
        $gpuStatus = $_.Exception.Message
        $remaining = $Seconds - $watch.Elapsed.TotalSeconds
        if ($remaining -gt 0) { Start-Sleep -Milliseconds ([int]($remaining * 1000)) }
    }
}
else { Start-Sleep -Seconds $Seconds }
$rendererProcess.Refresh()
if ($rendererProcess.HasExited) { throw 'The renderer exited during measurement.' }
$elapsed = $watch.Elapsed.TotalSeconds
$cpuSeconds = $rendererProcess.TotalProcessorTime.TotalSeconds - $startCpu

[pscustomobject]@{
    ProcessId = $ProcessId
    Seconds = [Math]::Round($elapsed, 2)
    CpuSeconds = [Math]::Round($cpuSeconds, 4)
    CpuPercentOfMachine = [Math]::Round(100 * $cpuSeconds / $elapsed / [Environment]::ProcessorCount, 3)
    WorkingSetMiB = [Math]::Round($rendererProcess.WorkingSet64 / 1MB, 2)
    WorkingSetChangeMiB = [Math]::Round(($rendererProcess.WorkingSet64 - $startMemory) / 1MB, 2)
    PrivateBytesMiB = [Math]::Round($rendererProcess.PrivateMemorySize64 / 1MB, 2)
    Handles = $rendererProcess.HandleCount
    HandleChange = $rendererProcess.HandleCount - $startHandles
    GpuStatus = $gpuStatus
    Gpu3DEngineAveragePercent = if ($gpuSamples.Count) { [Math]::Round(($gpuSamples | Measure-Object -Average).Average, 3) } else { $null }
    Gpu3DEnginePeakPercent = if ($gpuSamples.Count) { ($gpuSamples | Measure-Object -Maximum).Maximum } else { $null }
    DedicatedGpuMiB = if ($gpuMemory.Count) { [Math]::Round(($gpuMemory | Measure-Object DedicatedUsage -Sum).Sum / 1MB, 2) } else { $null }
    SharedGpuMiB = if ($gpuMemory.Count) { [Math]::Round(($gpuMemory | Measure-Object SharedUsage -Sum).Sum / 1MB, 2) } else { $null }
}
