param (
    [string]$Configuration = "Debug",
    [string]$Output = ""
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$appOutput = Join-Path $root "Tomighty.Windows\bin\$Configuration"

if (-not $Output) {
    $Output = Join-Path $root "build\timer-window-smoke.png"
}

New-Item -ItemType Directory -Path (Split-Path -Parent $Output) -Force | Out-Null

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Set-Location $appOutput
[System.Reflection.Assembly]::LoadFrom((Resolve-Path ".\Tomighty.Core.dll")) | Out-Null
[System.Reflection.Assembly]::LoadFrom((Resolve-Path ".\Tomighty.Windows.exe")) | Out-Null

[System.Windows.Forms.Application]::EnableVisualStyles()

$form = New-Object Tomighty.Windows.Timer.TimerWindow
$form.UpdateTitle("Pomodoro")
$form.UpdateTimeDisplay("25:00")
$form.UpdateColorScheme([System.Drawing.Color]::FromArgb(197, 47, 61))
$form.SetTimerAction("Interrupt", [System.Action]{})
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
$form.Location = New-Object System.Drawing.Point(80, 80)
$form.Show()

[System.Windows.Forms.Application]::DoEvents()
Start-Sleep -Milliseconds 700
[System.Windows.Forms.Application]::DoEvents()

$bounds = New-Object System.Drawing.Rectangle(0, 0, $form.Width, $form.Height)
$bitmap = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)

$form.DrawToBitmap($bitmap, $bounds)
$bitmap.Save($Output, [System.Drawing.Imaging.ImageFormat]::Png)

$graphics.Dispose()
$bitmap.Dispose()
$form.Close()
$form.Dispose()

Write-Output $Output
