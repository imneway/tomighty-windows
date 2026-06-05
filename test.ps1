param (
    [switch]$SkipRelease
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $root "Tomighty.sln"
$msbuild = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"

if (-not (Test-Path $msbuild)) {
    $msbuildCommand = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if (-not $msbuildCommand) {
        throw "MSBuild.exe was not found. Install Visual Studio Build Tools or add MSBuild to PATH."
    }

    $msbuild = $msbuildCommand.Source
}

Push-Location $root
try {
    & $msbuild $solution /t:Rebuild /p:Configuration=Debug
    & ".\packages\NUnit.ConsoleRunner.3.4.1\tools\nunit3-console.exe" ".\Tomighty.Core.Test\bin\Debug\Tomighty.Core.Test.dll"
    powershell -STA -NoProfile -ExecutionPolicy Bypass -File ".\tools\smoke-timer-window.ps1" -Configuration Debug
    powershell -STA -NoProfile -ExecutionPolicy Bypass -File ".\tools\smoke-dialogs.ps1" -Configuration Debug

    if (-not $SkipRelease) {
        powershell -NoProfile -ExecutionPolicy Bypass -File ".\publish.ps1" -Mode Zip -Configuration Release
        powershell -NoProfile -ExecutionPolicy Bypass -File ".\publish.ps1" -Mode Msix -Configuration Release
    }
}
finally {
    Pop-Location
}
