param(
    [int]$Seconds = 60,
    [string]$ProjectPath = "benchmarks\ui_lag_bench_project\project.aaep",
    [string]$Scenario = "",
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [int]$CloseTimeoutSeconds = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-RepositoryPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return (Resolve-Path -LiteralPath $Path).Path
    }

    return (Resolve-Path -LiteralPath (Join-Path $repositoryRoot $Path)).Path
}

function Format-ByteSize {
    param(
        [long]$Bytes
    )

    if ($Bytes -lt 0) {
        return "-{0}" -f (Format-ByteSize -Bytes ([Math]::Abs($Bytes)))
    }

    if ($Bytes -ge 1GB) {
        return "{0:N1} GB" -f ($Bytes / 1GB)
    }

    if ($Bytes -ge 1MB) {
        return "{0:N1} MB" -f ($Bytes / 1MB)
    }

    if ($Bytes -ge 1KB) {
        return "{0:N1} KB" -f ($Bytes / 1KB)
    }

    return "$Bytes B"
}

function Quote-ProcessArgument {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    return '"' + ($Value -replace '\\', '\\' -replace '"', '\"') + '"'
}

if ($Seconds -lt 1) {
    throw "Benchmark duration must be at least 1 second."
}

$scriptDirectory = Split-Path -Parent $PSCommandPath
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $scriptDirectory "..")).Path
$resolvedProjectPath = Resolve-RepositoryPath -Path $ProjectPath
$solutionPath = Join-Path $repositoryRoot "HornetStudio.sln"
$appDirectory = Join-Path $repositoryRoot "src\HornetStudio\bin\$Configuration\net9.0-windows"
$appPath = Join-Path $appDirectory "HornetStudio.exe"

if ([string]::IsNullOrWhiteSpace($Scenario)) {
    $Scenario = "UiLagBench-{0}" -f (Get-Date -Format "yyyyMMdd-HHmmss")
}

if (-not $NoBuild) {
    dotnet build $solutionPath --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "Solution build failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $appPath)) {
    throw "HornetStudio executable was not found: $appPath"
}

$logDirectory = Join-Path $appDirectory "logs"
$logFile = Join-Path $logDirectory ("host-{0}.log" -f (Get-Date -Format "yyyyMMdd"))
$preRunLineCount = 0
if (Test-Path -LiteralPath $logFile) {
    $preRunLineCount = (Get-Content -LiteralPath $logFile | Measure-Object -Line).Lines
}

$arguments = @(
    Quote-ProcessArgument -Value "--ui-benchmark"
    Quote-ProcessArgument -Value "--start-project"
    Quote-ProcessArgument -Value $resolvedProjectPath
) -join " "

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $appPath
$startInfo.Arguments = $arguments
$startInfo.WorkingDirectory = $appDirectory
$startInfo.UseShellExecute = $false
$startInfo.EnvironmentVariables["HORNETSTUDIO_UI_BENCHMARK"] = "1"
$startInfo.EnvironmentVariables["HORNETSTUDIO_UI_BENCHMARK_SECONDS"] = $Seconds.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$startInfo.EnvironmentVariables["HORNETSTUDIO_UI_BENCHMARK_SCENARIO"] = $Scenario

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo

Write-Output "Starting HornetStudio benchmark."
Write-Output "Scenario: $Scenario"
Write-Output "Project: $resolvedProjectPath"
Write-Output "DurationSeconds: $Seconds"

if (-not $process.Start()) {
    throw "HornetStudio process could not be started."
}

$cpuSamples = New-Object System.Collections.Generic.List[double]
$workingSetSamples = New-Object System.Collections.Generic.List[long]
$privateMemorySamples = New-Object System.Collections.Generic.List[long]
$previousCpu = [TimeSpan]::Zero
$previousTimestamp = Get-Date
$benchmarkStartTime = $previousTimestamp
$deadline = (Get-Date).AddSeconds($Seconds)
$initialWorkingSet = 0L
$initialPrivateMemory = 0L
$lastProgressReportSeconds = -1

try {
    $process.Refresh()
    $initialWorkingSet = $process.WorkingSet64
    $initialPrivateMemory = $process.PrivateMemorySize64
    $workingSetSamples.Add($initialWorkingSet)
    $privateMemorySamples.Add($initialPrivateMemory)

    while ((Get-Date) -lt $deadline -and -not $process.HasExited) {
        Start-Sleep -Seconds 1
        $process.Refresh()

        $currentTimestamp = Get-Date
        $currentCpu = $process.TotalProcessorTime
        $elapsedMs = ($currentTimestamp - $previousTimestamp).TotalMilliseconds
        if ($elapsedMs -gt 0) {
            $cpuPercent = (($currentCpu - $previousCpu).TotalMilliseconds / ($elapsedMs * [Environment]::ProcessorCount)) * 100
            $cpuSamples.Add([Math]::Max(0, $cpuPercent))
        }

        $workingSetSamples.Add($process.WorkingSet64)
        $privateMemorySamples.Add($process.PrivateMemorySize64)
        $previousCpu = $currentCpu
        $previousTimestamp = $currentTimestamp

        $elapsedSeconds = [int][Math]::Floor(($currentTimestamp - $benchmarkStartTime).TotalSeconds)
        if ($elapsedSeconds -ge 0 -and ($lastProgressReportSeconds -lt 0 -or ($elapsedSeconds - $lastProgressReportSeconds) -ge 5)) {
            $remainingSeconds = [int][Math]::Ceiling([Math]::Max(0, ($deadline - $currentTimestamp).TotalSeconds))
            Write-Output ("ProgressSeconds: {0} RemainingSeconds: {1}" -f $elapsedSeconds, $remainingSeconds)
            $lastProgressReportSeconds = $elapsedSeconds
        }
    }

    Start-Sleep -Seconds 2
}
finally {
    if (-not $process.HasExited) {
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit($CloseTimeoutSeconds * 1000)) {
            $process.Kill()
            $process.WaitForExit()
        }
    }

    $process.Dispose()
}

Start-Sleep -Seconds 1

$newLogLines = @()
if (Test-Path -LiteralPath $logFile) {
    $allLogLines = @(Get-Content -LiteralPath $logFile)
    $newLogLines = @($allLogLines | Select-Object -Skip $preRunLineCount)
}

$benchmarkLines = @($newLogLines | Where-Object {
    $_ -like "*$Scenario*" -and (
        $_ -like "*UI benchmark summary*" -or
        $_ -like "*UI benchmark browser summary*" -or
    $_ -like "*UI benchmark signal pipeline summary*" -or
        $_ -like "*UI benchmark chart summary*" -or
        $_ -like "*UI benchmark timing summary*")
})

$issueLines = @($newLogLines | Where-Object {
    $_ -match "\[(ERR|FTL)\]" -or
    $_ -match "\b(Error|Fatal|Exception|Unhandled)\b"
})

$warningLines = @($newLogLines | Where-Object {
    $_ -match "\[WRN\]" -or
    $_ -match "\b(Warning|Warn)\b"
})

$averageCpu = if ($cpuSamples.Count -gt 0) { ($cpuSamples | Measure-Object -Average).Average } else { 0 }
$peakCpu = if ($cpuSamples.Count -gt 0) { ($cpuSamples | Measure-Object -Maximum).Maximum } else { 0 }
$peakWorkingSet = if ($workingSetSamples.Count -gt 0) { ($workingSetSamples | Measure-Object -Maximum).Maximum } else { 0 }
$peakPrivateMemory = if ($privateMemorySamples.Count -gt 0) { ($privateMemorySamples | Measure-Object -Maximum).Maximum } else { 0 }
$finalWorkingSet = if ($workingSetSamples.Count -gt 0) { $workingSetSamples[$workingSetSamples.Count - 1] } else { 0 }
$finalPrivateMemory = if ($privateMemorySamples.Count -gt 0) { $privateMemorySamples[$privateMemorySamples.Count - 1] } else { 0 }
$workingSetDelta = $finalWorkingSet - $initialWorkingSet
$privateMemoryDelta = $finalPrivateMemory - $initialPrivateMemory

Write-Output ""
Write-Output "Benchmark result"
Write-Output "Scenario: $Scenario"
Write-Output "Project: $resolvedProjectPath"
Write-Output "Configuration: $Configuration"
Write-Output "DurationSeconds: $Seconds"
Write-Output ("AverageCpuPercent: {0:N1}" -f $averageCpu)
Write-Output ("PeakCpuPercent: {0:N1}" -f $peakCpu)
Write-Output ("StartWorkingSet: {0}" -f (Format-ByteSize -Bytes $initialWorkingSet))
Write-Output ("EndWorkingSet: {0}" -f (Format-ByteSize -Bytes $finalWorkingSet))
Write-Output ("DeltaWorkingSet: {0}" -f (Format-ByteSize -Bytes $workingSetDelta))
Write-Output ("PeakWorkingSet: {0}" -f (Format-ByteSize -Bytes $peakWorkingSet))
Write-Output ("StartPrivateMemory: {0}" -f (Format-ByteSize -Bytes $initialPrivateMemory))
Write-Output ("EndPrivateMemory: {0}" -f (Format-ByteSize -Bytes $finalPrivateMemory))
Write-Output ("DeltaPrivateMemory: {0}" -f (Format-ByteSize -Bytes $privateMemoryDelta))
Write-Output ("PeakPrivateMemory: {0}" -f (Format-ByteSize -Bytes $peakPrivateMemory))
Write-Output "IssueLineCount: $($issueLines.Count)"
Write-Output "WarningLineCount: $($warningLines.Count)"
Write-Output "LogFile: $logFile"

if ($benchmarkLines.Count -eq 0) {
    Write-Output "BenchmarkLines: none"
}
else {
    Write-Output ""
    Write-Output "BenchmarkLines:"
    foreach ($line in $benchmarkLines) {
        Write-Output $line
    }
}

if ($warningLines.Count -gt 0) {
    Write-Output ""
    Write-Output "WarningLines:"
    foreach ($line in ($warningLines | Select-Object -First 20)) {
        Write-Output $line
    }
}

if ($issueLines.Count -gt 0) {
    Write-Output ""
    Write-Output "IssueLines:"
    foreach ($line in ($issueLines | Select-Object -First 20)) {
        Write-Output $line
    }
}
