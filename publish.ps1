param (
    [ValidateSet("All", "Zip", "Installer", "Msix")]
    [string]$Mode = "All",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidatePattern("^[A-Za-z0-9._-]*$")]
    [string]$Tag = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $root "Tomighty.sln"
$dist = Join-Path $root "dist"
$build = Join-Path $root "build"

function Find-MSBuild {
    $candidates = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    $fromPath = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($fromPath) {
        return $fromPath.Source
    }

    throw "MSBuild.exe was not found. Install Visual Studio Build Tools or add MSBuild to PATH."
}

function Find-MakeAppx {
    $fromPath = Get-Command makeappx.exe -ErrorAction SilentlyContinue
    if ($fromPath) {
        return $fromPath.Source
    }

    $kits = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "${env:ProgramFiles}\Windows Kits\10\bin"
    )

    foreach ($kit in $kits) {
        if (-not (Test-Path $kit)) {
            continue
        }

        $candidate = Get-ChildItem -Path $kit -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match "\\x64\\makeappx\.exe$" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1

        if ($candidate) {
            return $candidate.FullName
        }
    }

    throw "makeappx.exe was not found. Install Windows SDK to build an MSIX artifact."
}

function Copy-IfExists($source, $destination) {
    if (Test-Path $source) {
        Copy-Item $source $destination -Force
    }
}

function New-ResizedPng($source, $destination, $width, $height) {
    Add-Type -AssemblyName System.Drawing

    $image = [System.Drawing.Image]::FromFile($source)
    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.DrawImage($image, 0, 0, $width, $height)

    $bitmap.Save($destination, [System.Drawing.Imaging.ImageFormat]::Png)

    $graphics.Dispose()
    $bitmap.Dispose()
    $image.Dispose()
}

function Convert-ToMsixVersion($fileVersion) {
    $parts = $fileVersion.Split(".")
    while ($parts.Length -lt 4) {
        $parts += "0"
    }

    return ($parts[0..3] -join ".")
}

function New-MsixManifest($destination, $version) {
    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap uap10 rescap">
  <Identity Name="Tomighty.Windows" Publisher="CN=Tomighty" Version="$version" ProcessorArchitecture="x64" />
  <Properties>
    <DisplayName>Tomighty</DisplayName>
    <PublisherDisplayName>Tomighty</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.22000.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Resources>
    <Resource Language="en-us" />
  </Resources>
  <Applications>
    <Application Id="Tomighty" Executable="Tomighty.Windows.exe" EntryPoint="Windows.FullTrustApplication" uap10:RuntimeBehavior="packagedClassicApp" uap10:TrustLevel="mediumIL">
      <uap:VisualElements DisplayName="Tomighty" Description="Tomighty" BackgroundColor="#C52F3D" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png" />
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"@

    Set-Content -Path $destination -Value $manifest -Encoding UTF8
}

$msbuild = Find-MSBuild
& $msbuild $solution /t:Rebuild /p:Configuration=$Configuration

$appOutput = Join-Path $root "Tomighty.Windows\bin\$Configuration"
$swapOutput = Join-Path $root "Tomighty.Update.Swap\bin\$Configuration"
$appExe = Join-Path $appOutput "Tomighty.Windows.exe"
$version = (Get-Item $appExe).VersionInfo.FileVersion

$artifact = "tomighty-windows-$version"
if ($Tag) {
    $artifact = "$artifact-$Tag"
}

$artifactDir = Join-Path $build $artifact
$zipFile = Join-Path $dist "$artifact.zip"
$msixRoot = Join-Path $build "$artifact-msix"
$msixFile = Join-Path $dist "$artifact.msix"

New-Item -ItemType Directory -Path $build -Force | Out-Null
New-Item -ItemType Directory -Path $dist -Force | Out-Null

if (Test-Path $artifactDir) {
    Remove-Item $artifactDir -Recurse -Force
}

if (($Mode -eq "All" -or $Mode -eq "Zip") -and (Test-Path $zipFile)) {
    Remove-Item $zipFile -Force
}

if (($Mode -eq "All" -or $Mode -eq "Msix") -and (Test-Path $msixRoot)) {
    Remove-Item $msixRoot -Recurse -Force
}

if (($Mode -eq "All" -or $Mode -eq "Msix") -and (Test-Path $msixFile)) {
    Remove-Item $msixFile -Force
}

New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null

Copy-Item (Join-Path $root "LICENSE.txt") $artifactDir -Force
Copy-Item (Join-Path $root "NOTICE.txt") $artifactDir -Force
Copy-Item (Join-Path $appOutput "Tomighty.Windows.exe") $artifactDir -Force
Copy-Item (Join-Path $appOutput "Tomighty.Windows.exe.config") $artifactDir -Force
Copy-Item (Join-Path $appOutput "Tomighty.Core.dll") $artifactDir -Force
Copy-IfExists (Join-Path $appOutput "Microsoft.Toolkit.Uwp.Notifications.dll") $artifactDir
Copy-IfExists (Join-Path $swapOutput "Tomighty.Update.Swap.exe") $artifactDir
Copy-Item (Join-Path $appOutput "Resources") (Join-Path $artifactDir "Resources") -Recurse -Force

if ($Mode -eq "All" -or $Mode -eq "Zip") {
    & (Join-Path $root "pack.ps1") $artifactDir $zipFile
    Write-Host "Portable ZIP: $zipFile"
}

if ($Mode -eq "All" -or $Mode -eq "Installer") {
    $makensis = Get-Command makensis.exe -ErrorAction SilentlyContinue
    if ($makensis) {
        & $makensis.Source `
            "-DPRODUCT_NAME=Tomighty" `
            "-DPRODUCT_VERSION=$version" `
            "-DPRODUCT_FILE_VERSION=$version" `
            "-DARTIFACT_NAME=$artifact" `
            "-DBUILD_DIR=$artifactDir" `
            (Join-Path $root "setup.nsi")
        Write-Host "Installer output written by NSIS in dist\."
    }
    else {
        Write-Warning "NSIS was not found. Skipping installer mode."
    }
}

if ($Mode -eq "All" -or $Mode -eq "Msix") {
    $makeAppx = Find-MakeAppx
    $msixVersion = Convert-ToMsixVersion $version
    $assetsDir = Join-Path $msixRoot "Assets"
    $sourceLogo = Join-Path $appOutput "Resources\Toasts\image_toast_tomato_red.png"

    Copy-Item $artifactDir $msixRoot -Recurse -Force
    New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null

    New-ResizedPng $sourceLogo (Join-Path $assetsDir "Square44x44Logo.png") 44 44
    New-ResizedPng $sourceLogo (Join-Path $assetsDir "Square150x150Logo.png") 150 150
    New-ResizedPng $sourceLogo (Join-Path $assetsDir "StoreLogo.png") 50 50
    New-MsixManifest (Join-Path $msixRoot "AppxManifest.xml") $msixVersion

    & $makeAppx pack /d $msixRoot /p $msixFile /o
    Write-Host "Unsigned MSIX: $msixFile"
}
