param (
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$appOutput = Join-Path $root "Tomighty.Windows\bin\$Configuration"
$hostSource = Join-Path $PSScriptRoot "PreferencesProbe.cs"
$hostExe = Join-Path $appOutput "PreferencesProbe.exe"
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
    /reference:System.Runtime.Serialization.dll `
    /reference:"$appOutput\Tomighty.Core.dll" `
    /reference:"$appOutput\Tomighty.Windows.exe" `
    $hostSource

if ($LASTEXITCODE -ne 0) {
    throw "PreferencesProbe compilation failed with exit code $LASTEXITCODE."
}

Push-Location $appOutput
try {
    & $hostExe @args
}
finally {
    Pop-Location
}
