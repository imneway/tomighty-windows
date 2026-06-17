param (
    [string]$Configuration = "Debug",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$appOutput = Join-Path $root "Tomighty.Windows\bin\$Configuration"
$buildOutput = Join-Path $root "build"
$hostSource = Join-Path $PSScriptRoot "TrayRuntimeSmokeHost.cs"
$hostExe = Join-Path $appOutput "TrayRuntimeSmokeHost.exe"

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $buildOutput "tray-runtime-smoke"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$csc = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe"
if (-not (Test-Path $csc)) {
    $cscCommand = Get-Command csc.exe -ErrorAction SilentlyContinue
    if (-not $cscCommand) {
        throw "csc.exe was not found. Install Visual Studio Build Tools or add csc to PATH."
    }

    $csc = $cscCommand.Source
}

& $csc `
    /nologo `
    /target:exe `
    /out:$hostExe `
    /reference:System.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:"$appOutput\Tomighty.Core.dll" `
    /reference:"$appOutput\Tomighty.Windows.exe" `
    $hostSource

if ($LASTEXITCODE -ne 0) {
    throw "TrayRuntimeSmokeHost compilation failed with exit code $LASTEXITCODE."
}

Push-Location $appOutput
try {
    & $hostExe $OutputDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "TrayRuntimeSmokeHost failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

Write-Output $OutputDirectory
