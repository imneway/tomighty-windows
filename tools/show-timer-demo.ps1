param (
    [string]$Configuration = "Debug",
    [ValidateSet("primary", "secondary", "small")]
    [string]$Screen = "primary"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$appOutput = Join-Path $root "Tomighty.Windows\bin\$Configuration"
$buildOutput = Join-Path $root "build"
$hostSource = Join-Path $PSScriptRoot "TimerDemoHost.cs"
$hostConfig = Join-Path $PSScriptRoot "TimerDemoHost.exe.config"
$hostExe = Join-Path $appOutput "TomightyTimerDemo.exe"
$probeOutput = Join-Path $buildOutput "timer-demo-probe.txt"

New-Item -ItemType Directory -Path $buildOutput -Force | Out-Null
Remove-Item -Path $probeOutput -Force -ErrorAction SilentlyContinue

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
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:"$appOutput\Tomighty.Core.dll" `
    /reference:"$appOutput\Tomighty.Windows.exe" `
    $hostSource

Copy-Item -Path $hostConfig -Destination "$hostExe.config" -Force

Push-Location $appOutput
try {
    Start-Process -FilePath $hostExe -ArgumentList @($Screen, "`"$probeOutput`"") -WorkingDirectory $appOutput
}
finally {
    Pop-Location
}

Write-Output $probeOutput
