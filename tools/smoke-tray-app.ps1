param (
    [string]$Configuration = "Debug",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$appOutput = Join-Path $root "Tomighty.Windows\bin\$Configuration"
$buildOutput = Join-Path $root "build"
$appExe = Join-Path $appOutput "Tomighty.Windows.exe"
$hostSource = Join-Path $PSScriptRoot "TrayAppSmokeHost.cs"
$hostExe = Join-Path $appOutput "TrayAppSmokeHost.exe"
$frameworkRef = "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
$preferencesDirectory = Join-Path $env:LOCALAPPDATA "Tomighty"
$preferencesPath = Join-Path $preferencesDirectory "preferences.json"
$hadPreferences = Test-Path $preferencesPath
$backupPath = Join-Path $buildOutput "preferences-before-tray-app-smoke.json"
$process = $null

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $buildOutput "tray-app-smoke"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $preferencesDirectory -Force | Out-Null

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
    /reference:"$frameworkRef\WindowsBase.dll" `
    /reference:"$frameworkRef\UIAutomationClient.dll" `
    /reference:"$frameworkRef\UIAutomationTypes.dll" `
    $hostSource

if ($LASTEXITCODE -ne 0) {
    throw "TrayAppSmokeHost compilation failed with exit code $LASTEXITCODE."
}

try {
    if ($hadPreferences) {
        Copy-Item -LiteralPath $preferencesPath -Destination $backupPath -Force
    }

    powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "probe-preferences.ps1") -Configuration $Configuration write-smoke | Out-Null

    $process = Start-Process -FilePath $appExe -PassThru -WindowStyle Hidden
    Start-Sleep -Milliseconds 800

    & $hostExe $process.Id $OutputDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "TrayAppSmokeHost failed with exit code $LASTEXITCODE."
    }
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }

    if ($hadPreferences) {
        Copy-Item -LiteralPath $backupPath -Destination $preferencesPath -Force
    }
    elseif (Test-Path $preferencesPath) {
        Remove-Item -LiteralPath $preferencesPath -Force
    }
}

Write-Output $OutputDirectory
