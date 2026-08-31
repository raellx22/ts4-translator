param(
    [string]$RuntimeRoot = (Join-Path $PSScriptRoot ".runtime")
)

$ErrorActionPreference = "Stop"
$pythonVersion = "3.12.10"
$pythonZipName = "python-$pythonVersion-embed-amd64.zip"
$pythonUrl = "https://www.python.org/ftp/python/$pythonVersion/$pythonZipName"
$pythonMd5 = "fe8ef205f2e9c3ba44d0cf9954e1abd3"
$getPipUrl = "https://bootstrap.pypa.io/get-pip.py"

$downloads = Join-Path $RuntimeRoot "downloads"
$pythonHome = Join-Path $RuntimeRoot "python"
$pythonZip = Join-Path $downloads $pythonZipName
$getPip = Join-Path $downloads "get-pip.py"

New-Item -ItemType Directory -Force -Path $downloads | Out-Null
if (-not (Test-Path -LiteralPath $pythonZip)) {
    Invoke-WebRequest -Uri $pythonUrl -OutFile $pythonZip
}

$actualMd5 = (Get-FileHash -LiteralPath $pythonZip -Algorithm MD5).Hash.ToLowerInvariant()
if ($actualMd5 -ne $pythonMd5) {
    throw "Python archive hash mismatch. Expected $pythonMd5, got $actualMd5."
}

if (-not (Test-Path -LiteralPath (Join-Path $pythonHome "python.exe"))) {
    New-Item -ItemType Directory -Force -Path $pythonHome | Out-Null
    Expand-Archive -LiteralPath $pythonZip -DestinationPath $pythonHome -Force

    $pthFile = Join-Path $pythonHome "python312._pth"
    $pth = Get-Content -LiteralPath $pthFile -Raw
    $pth = $pth.Replace("#import site", "import site")
    [System.IO.File]::WriteAllText($pthFile, $pth, [System.Text.UTF8Encoding]::new($false))
}

if (-not (Test-Path -LiteralPath $getPip)) {
    Invoke-WebRequest -Uri $getPipUrl -OutFile $getPip
}

$pythonExe = Join-Path $pythonHome "python.exe"
& $pythonExe $getPip --disable-pip-version-check
if ($LASTEXITCODE -ne 0) { throw "get-pip.py failed with exit code $LASTEXITCODE." }

# Argos declares both Stanza and spaCy even when its light MiniSBD path is selected. Installing the
# default dependency graph would pull PyTorch and hundreds of megabytes which this application never
# uses. Install only the runtime used by ARGOS_CHUNK_TYPE=MINISBD, then install Argos without deps.
& $pythonExe -m pip install --disable-pip-version-check --no-warn-script-location `
    "ctranslate2>=4.0,<5" "minisbd==0.9.5" "packaging" "sacremoses>=0.0.53,<0.2" `
    "sentencepiece>=0.2.0,<0.3" "requests" "pyyaml>=5.3,<7"
if ($LASTEXITCODE -ne 0) { throw "Argos runtime dependency install failed with exit code $LASTEXITCODE." }

& $pythonExe -m pip install --disable-pip-version-check --no-warn-script-location --no-deps "argostranslate==1.11.0"
if ($LASTEXITCODE -ne 0) { throw "Argos Translate install failed with exit code $LASTEXITCODE." }

& $pythonExe -c "import importlib.metadata, argostranslate, ctranslate2, minisbd; print('argostranslate=' + importlib.metadata.version('argostranslate')); print('ctranslate2=' + ctranslate2.__version__)"
if ($LASTEXITCODE -ne 0) { throw "Argos Translate import validation failed with exit code $LASTEXITCODE." }

Write-Output $pythonExe
