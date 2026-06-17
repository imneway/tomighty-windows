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

function Invoke-CheckedCommand {
    param (
        [scriptblock]$Command,
        [string]$Name
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

Push-Location $root
try {
    Invoke-CheckedCommand { & $msbuild $solution /t:Rebuild /p:Configuration=Debug } "Debug build"
    Invoke-CheckedCommand { & ".\packages\NUnit.ConsoleRunner.3.4.1\tools\nunit3-console.exe" ".\Tomighty.Core.Test\bin\Debug\Tomighty.Core.Test.dll" } "NUnit"
    Invoke-CheckedCommand { powershell -STA -NoProfile -ExecutionPolicy Bypass -File ".\tools\smoke-dialogs.ps1" -Configuration Debug } "Dialog smoke"
    Invoke-CheckedCommand { powershell -STA -NoProfile -ExecutionPolicy Bypass -File ".\tools\smoke-timer-window.ps1" -Configuration Debug } "Timer smoke"

    if (-not $SkipRelease) {
        Invoke-CheckedCommand { powershell -NoProfile -ExecutionPolicy Bypass -File ".\publish.ps1" -Mode Zip -Configuration Release } "Zip publish"
        Invoke-CheckedCommand { powershell -NoProfile -ExecutionPolicy Bypass -File ".\publish.ps1" -Mode Msix -Configuration Release } "MSIX publish"
    }
}
finally {
    Pop-Location
}
