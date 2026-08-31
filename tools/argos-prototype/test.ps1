param(
    [string]$RuntimeRoot = (Join-Path $PSScriptRoot ".runtime")
)

$ErrorActionPreference = "Stop"
$pythonExe = Join-Path $RuntimeRoot "python\python.exe"
$sidecar = Join-Path $PSScriptRoot "sidecar.py"
$modelArchive = Join-Path $RuntimeRoot "downloads\translate-en_pb-1_9.argosmodel"
$modelUrl = "https://argos-net.com/v1/translate-en_pb-1_9.argosmodel"
$packagesDir = Join-Path $RuntimeRoot "data\packages"

if (-not (Test-Path -LiteralPath $pythonExe)) {
    throw "Runtime not found. Run bootstrap.ps1 first."
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $modelArchive), $packagesDir | Out-Null
if (-not (Test-Path -LiteralPath $modelArchive)) {
    Invoke-WebRequest -Uri $modelUrl -OutFile $modelArchive
}

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $pythonExe
$startInfo.ArgumentList.Add("-u")
$startInfo.ArgumentList.Add($sidecar)
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.CreateNoWindow = $true
$startInfo.StandardInputEncoding = [System.Text.UTF8Encoding]::new($false)
$startInfo.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
$startInfo.Environment["ARGOS_PACKAGES_DIR"] = $packagesDir
$startInfo.Environment["ARGOS_DEVICE_TYPE"] = "cpu"
$startInfo.Environment["ARGOS_CHUNK_TYPE"] = "MINISBD"
$startInfo.Environment["ARGOS_BEAM_SIZE"] = "1"
$startInfo.Environment["ARGOS_BATCH_SIZE"] = "64"
$startInfo.Environment["ARGOS_INTER_THREADS"] = "1"
$startInfo.Environment["ARGOS_INTRA_THREADS"] = "0"
$startInfo.Environment["PYTHONUTF8"] = "1"
$startInfo.Environment["PYTHONIOENCODING"] = "utf-8"
$startInfo.Environment["XDG_DATA_HOME"] = (Join-Path $RuntimeRoot "data")
$startInfo.Environment["XDG_CONFIG_HOME"] = (Join-Path $RuntimeRoot "config")
$startInfo.Environment["XDG_CACHE_HOME"] = (Join-Path $RuntimeRoot "cache")

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo
if (-not $process.Start()) { throw "Could not start Argos sidecar." }

function Invoke-Sidecar([hashtable]$request) {
    $line = $request | ConvertTo-Json -Compress -Depth 10
    $process.StandardInput.WriteLine($line)
    $process.StandardInput.Flush()
    $responseLine = $process.StandardOutput.ReadLine()
    if ($null -eq $responseLine) {
        $stderr = $process.StandardError.ReadToEnd()
        throw "Sidecar ended unexpectedly: $stderr"
    }

    $response = $responseLine | ConvertFrom-Json
    if (-not $response.ok) {
        throw "Sidecar error ($($response.type)): $($response.error)`n$($response.trace)"
    }
    return $response
}

try {
    Invoke-Sidecar @{ id = "ping"; command = "ping" } | ConvertTo-Json -Compress
    $status = Invoke-Sidecar @{ id = "status-before"; command = "status" }
    if (-not ($status.packages | Where-Object { $_.from -eq "en" -and $_.to -eq "pb" })) {
        Invoke-Sidecar @{ id = "install"; command = "install_model"; path = $modelArchive } | ConvertTo-Json -Compress
    }

    $items = @(
        @{ id = "1"; text = "Hello! My name is __TS4_TOKEN_0001__." },
        @{ id = "2"; text = "Choose how often this interaction should be available." },
        @{ id = "3"; text = "Your Sim cannot perform this action while sleeping." },
        @{ id = "4"; text = "Enable pregnancy settings for all active households." }
    )
    Invoke-Sidecar @{ id = "translate-cold"; command = "translate"; source = "en"; target = "pb"; workers = 1; items = $items } |
        ConvertTo-Json -Depth 10
    $throughputItems = 1..200 | ForEach-Object {
        @{ id = $_.ToString(); text = $items[($_ - 1) % $items.Count].text }
    }
    Invoke-Sidecar @{ id = "translate-warm-200"; command = "translate"; source = "en"; target = "pb"; workers = 1; items = $throughputItems } |
        ConvertTo-Json -Depth 3
}
finally {
    if (-not $process.HasExited) {
        try { Invoke-Sidecar @{ id = "shutdown"; command = "shutdown" } | Out-Null } catch { }
        if (-not $process.WaitForExit(5000)) { $process.Kill($true) }
    }
    $process.Dispose()
}
