param (
    [string]$Configuration = "Debug",
    [string]$Output = ""
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$appOutput = Join-Path $root "Tomighty.Windows\bin\$Configuration"
$buildOutput = Join-Path $root "build"
$hostSource = Join-Path $PSScriptRoot "TimerSmokeHost.cs"
$hostConfig = Join-Path $PSScriptRoot "TimerSmokeHost.exe.config"
$hostExe = Join-Path $appOutput "TimerSmokeHost.exe"

if (-not $Output) {
    $Output = Join-Path $buildOutput "timer-window-smoke.png"
}

New-Item -ItemType Directory -Path (Split-Path -Parent $Output) -Force | Out-Null

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
    /target:winexe `
    /out:$hostExe `
    /reference:System.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:"$appOutput\Tomighty.Core.dll" `
    /reference:"$appOutput\Tomighty.Windows.exe" `
    $hostSource

if ($LASTEXITCODE -ne 0) {
    throw "TimerSmokeHost compilation failed with exit code $LASTEXITCODE."
}

Copy-Item -Path $hostConfig -Destination "$hostExe.config" -Force

Push-Location $appOutput
try {
    & $hostExe $Output
    if ($LASTEXITCODE -ne 0) {
        throw "TimerSmokeHost failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

Write-Output $Output
