param (
    [string]$Configuration = "Debug",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$appOutput = Join-Path $root "Tomighty.Windows\bin\$Configuration"

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root "build\ui-smoke"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Set-Location $appOutput
[System.Reflection.Assembly]::LoadFrom((Resolve-Path ".\Tomighty.Core.dll")) | Out-Null
$windowsAssembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path ".\Tomighty.Windows.exe"))

[System.Windows.Forms.Application]::EnableVisualStyles()

function Save-FormShot($form, $fileName) {
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.Location = New-Object System.Drawing.Point(80, 80)
    $form.Show()
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 500
    [System.Windows.Forms.Application]::DoEvents()

    $bounds = New-Object System.Drawing.Rectangle(0, 0, $form.Width, $form.Height)
    $bitmap = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
    $form.DrawToBitmap($bitmap, $bounds)
    $path = Join-Path $OutputDirectory $fileName
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)

    $bitmap.Dispose()
    $form.Close()
    $form.Dispose()

    Write-Output $path
}

$preferencesType = $windowsAssembly.GetType("Tomighty.Windows.Preferences.UserPreferences", $true)
$preferencesFormType = $windowsAssembly.GetType("Tomighty.Windows.Preferences.UserPreferencesForm", $true)
$aboutType = $windowsAssembly.GetType("Tomighty.Windows.About.AboutWindow", $true)
$errorType = $windowsAssembly.GetType("Tomighty.Windows.ErrorReportWindow", $true)

$preferences = [Activator]::CreateInstance($preferencesType)
$preferencesForm = [Activator]::CreateInstance($preferencesFormType, @($preferences))
Save-FormShot $preferencesForm "preferences.png"

$aboutForm = [Activator]::CreateInstance($aboutType)
Save-FormShot $aboutForm "about.png"

[System.Exception]$exception = New-Object System.Exception("UI smoke error")
$errorConstructor = $errorType.GetConstructor([Type[]]@([System.Exception]))
$constructorArguments = New-Object "System.Object[]" 1
$constructorArguments[0] = $exception
$errorForm = $errorConstructor.Invoke($constructorArguments)
Save-FormShot $errorForm "error.png"
