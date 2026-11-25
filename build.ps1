# Builds DeskWheel.exe on a Windows PC.
#   1. Install the .NET 10 SDK once:  winget install Microsoft.DotNet.SDK.10
#   (ffmpeg for video/audio is downloaded and shipped with DeskWheel automatically.)
#   3. In this folder:                powershell -ExecutionPolicy Bypass -File .\build.ps1
# Output: dist\DeskWheel-win-x64\DeskWheel.exe (no install needed; double-click to run).
#
# Options:
#   -Runtime win-arm64   build for ARM PCs
#   -SkipTests           don't run the core tests first
#   -Installer           also make dist\DeskWheel-<version>-<runtime>-setup.exe (needs Inno Setup:
#                        winget install JRSoftware.InnoSetup)
#   -RequireSigning      stop if nothing can be signed (release builds)
#   -Msix                also make dist\DeskWheel-<version>-<runtime>.msix for the Microsoft Store (free;
#                        Microsoft signs it, so Windows trusts it fully). Needs the Windows SDK (makeappx, makepri):
#                        winget install Microsoft.WindowsSDK.10.0.26100
#                        Store identity: WHEEL_MSIX_NAME, WHEEL_MSIX_PUBLISHER, WHEEL_MSIX_PUBLISHER_NAME
#                        (Partner Center › your app › Product identity). Try it locally with Developer Mode on:
#                        Add-AppxPackage -Register dist\msix-win-x64\AppxManifest.xml
#
# Signing (needed so Smart App Control and SmartScreen trust Desk Wheel on other PCs). Set ONE of these:
#   $env:WHEEL_SIGN_PFX = "C:\path\to\certificate.pfx";  $env:WHEEL_SIGN_PASSWORD = "..."
#   $env:WHEEL_SIGN_COMMAND = 'any command that signs one file, with {file} where the file goes'
#       e.g. a cloud signing tool:  'CodeSignTool sign -credential_id=... -input_file_path={file} -override'
#       or Azure Artifact Signing:  'signtool sign /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 /dlib Azure.CodeSigning.Dlib.dll /dmdf metadata.json {file}'
# Every unsigned .exe and .dll that ships is signed (DeskWheel.exe, ffmpeg.exe, pdfium.dll…), then the
# installer, including Inno Setup's own helper that runs from the temporary folder and the uninstaller.

param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [switch]$SkipTests,
    [switch]$Installer,
    [switch]$RequireSigning,
    [switch]$Msix
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "The .NET SDK isn't installed. Run: winget install Microsoft.DotNet.SDK.10 (then open a new terminal)."
}

# PDF engine (PDFium) for the target, and for this PC so the tests can use it.
function Get-Pdfium([string]$rid) {
    $dest = "engines\pdfium\$rid"
    if (Test-Path "$dest\pdfium.dll") { return }
    $asset = if ($rid -eq 'win-arm64') { 'pdfium-win-arm64.tgz' } else { 'pdfium-win-x64.tgz' }
    Write-Host "Downloading PDFium for $rid..." -ForegroundColor Cyan
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("pdfium-" + [Guid]::NewGuid())
    New-Item -ItemType Directory -Path $tmp | Out-Null
    Invoke-WebRequest "https://github.com/bblanchon/pdfium-binaries/releases/latest/download/$asset" -OutFile "$tmp\pdfium.tgz"
    tar -xzf "$tmp\pdfium.tgz" -C $tmp
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item "$tmp\bin\pdfium.dll" $dest
    Copy-Item "$tmp\LICENSE" "$dest\PDFium-LICENSE.txt" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force $tmp
}
Get-Pdfium $Runtime
Get-Pdfium 'win-x64'

# Video and audio engine shipped beside DeskWheel.exe: an LGPL build of FFmpeg (no GPL or nonfree parts),
# from BtbN/FFmpeg-Builds. H.264 uses Windows' own Media Foundation encoder.
function Get-Ffmpeg([string]$rid) {
    $dest = "engines\ffmpeg\$rid"
    if (Test-Path "$dest\ffmpeg.exe") { return }
    $arch = if ($rid -eq 'win-arm64') { 'winarm64' } else { 'win64' }
    $names = @("ffmpeg-n7.1-latest-$arch-lgpl-7.1.zip", "ffmpeg-master-latest-$arch-lgpl.zip")
    if ($arch -eq 'winarm64') { $names += @('ffmpeg-n7.1-latest-win64-lgpl-7.1.zip') } # x64 runs on ARM PCs too
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("ffmpeg-" + [Guid]::NewGuid())
    New-Item -ItemType Directory -Path $tmp | Out-Null
    $got = $null
    foreach ($name in $names) {
        try {
            Write-Host "Downloading $name..." -ForegroundColor Cyan
            Invoke-WebRequest "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/$name" -OutFile "$tmp\ffmpeg.zip"
            $got = $name
            break
        } catch { Write-Host "  not available, trying the next one" }
    }
    if (-not $got) { Write-Warning "Couldn't download ffmpeg; DeskWheel will use one installed on the PC, if any."; return }
    Expand-Archive "$tmp\ffmpeg.zip" -DestinationPath $tmp
    $root = Get-ChildItem $tmp -Directory | Select-Object -First 1
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item "$($root.FullName)\bin\ffmpeg.exe" $dest
    Copy-Item "$($root.FullName)\LICENSE.txt" "$dest\FFmpeg-LICENSE.txt" -ErrorAction SilentlyContinue
    # Licence check. An ARM64 ffmpeg can't run on an x64 build machine (GitHub's runners), so it's checked
    # by its build name and licence file there, and by its own build settings when it can run.
    $licence = Get-Content "$dest\FFmpeg-LICENSE.txt" -Raw -ErrorAction SilentlyContinue
    if ($got -notmatch '-lgpl' -or $licence -notmatch 'LESSER GENERAL PUBLIC') {
        Remove-Item -Recurse -Force $dest
        Write-Error "The downloaded ffmpeg isn't the LGPL build and must not ship."
    }
    $config = "(not run on this build machine: $rid)"
    try { $config = & "$dest\ffmpeg.exe" -hide_banner -buildconf 2>&1 | Out-String } catch { Write-Host "  (can't run $rid ffmpeg here; checked by name and licence)" }
    if ($config -match '--enable-(gpl|nonfree)') {
        Remove-Item -Recurse -Force $dest
        Write-Error "The downloaded ffmpeg contains GPL or nonfree parts and must not ship."
    }
    @(
        "FFmpeg for DeskWheel: $got from https://github.com/BtbN/FFmpeg-Builds (LGPL 2.1 or later; no GPL or nonfree parts).",
        "FFmpeg source: https://ffmpeg.org/download.html  ·  Build scripts: https://github.com/BtbN/FFmpeg-Builds",
        "", "Configuration:", $config
    ) | Set-Content "$dest\FFmpeg-SOURCE.txt"
    Remove-Item -Recurse -Force $tmp
}
Get-Ffmpeg $Runtime

if (-not $SkipTests) {
    Write-Host "Running core tests..." -ForegroundColor Cyan
    dotnet run --project tests\Wheel.Core.Tests -c Release
    if ($LASTEXITCODE -ne 0) { Write-Error "Core tests failed." }
}

Write-Host "Publishing DeskWheel.exe for $Runtime..." -ForegroundColor Cyan
dotnet publish src\Wheel.App\Wheel.App.csproj -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false `
    -o "dist\DeskWheel-$Runtime"
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed." }

$version = ([xml](Get-Content src\Wheel.App\Wheel.App.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

# ---------- signing ----------
# One small sign.cmd (taking the file as %1) is used for every file and by Inno Setup itself, so the
# installer, its helper in the temporary folder and the uninstaller are all signed the same way.
$signCmd = $null
function New-SignCommand {
    $template = $env:WHEEL_SIGN_COMMAND
    if (-not $template -and $env:WHEEL_SIGN_PFX) {
        $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1
        if (-not $signtool) { Write-Error "signtool.exe not found. Install the Windows SDK: winget install Microsoft.WindowsSDK.10.0.26100" }
        $template = "`"$($signtool.FullName)`" sign /fd sha256 /tr http://timestamp.digicert.com /td sha256 /f `"$env:WHEEL_SIGN_PFX`" /p `"$env:WHEEL_SIGN_PASSWORD`" {file}"
    }
    if (-not $template) { return $null }
    $cmd = Join-Path $PSScriptRoot "dist\sign.cmd"
    New-Item -ItemType Directory -Force -Path (Split-Path $cmd) | Out-Null
    Set-Content -Path $cmd -Encoding ASCII -Value "@echo off`r`n$($template.Replace('{file}', '%1'))`r`nexit /b %ERRORLEVEL%"
    return $cmd
}

function Sign-File([string]$path) {
    if (-not $signCmd) { return }
    & cmd.exe /c "`"$signCmd`" `"$((Resolve-Path $path).Path)`""
    if ($LASTEXITCODE -ne 0) { Write-Error "Signing $path failed." }
}

$signCmd = New-SignCommand
try {
if ($signCmd) {
    # Everything that runs: our exe and every bundled program or library that isn't signed by its maker already.
    $toSign = Get-ChildItem "dist\DeskWheel-$Runtime" -Recurse -Include *.exe, *.dll |
        Where-Object { (Get-AuthenticodeSignature $_.FullName).Status -ne 'Valid' }
    Write-Host "Signing $($toSign.Count) file(s)..." -ForegroundColor Cyan
    foreach ($f in $toSign) { Write-Host "  $($f.Name)"; Sign-File $f.FullName }
} elseif ($RequireSigning) {
    Write-Error "No signing set up (WHEEL_SIGN_PFX or WHEEL_SIGN_COMMAND). Release builds must be signed."
} else {
    Write-Warning "Not signed. It runs on this PC, but Windows Smart App Control / SmartScreen will block or warn on other PCs. See the notes at the top of build.ps1."
}

if ($Installer) {
    # Any installed Inno Setup (6 or newer), wherever it went; or ISCC on the PATH.
    $iscc = @("${env:ProgramFiles(x86)}", "$env:ProgramFiles", "$env:LOCALAPPDATA\Programs") | Where-Object { $_ } |
        ForEach-Object { Get-ChildItem -Path $_ -Filter 'Inno Setup*' -Directory -ErrorAction SilentlyContinue } |
        ForEach-Object { Join-Path $_.FullName 'ISCC.exe' } | Where-Object { Test-Path $_ } |
        Sort-Object -Descending | Select-Object -First 1
    if (-not $iscc) { $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
    if (-not $iscc) { Write-Error "Inno Setup isn't installed. Run: winget install JRSoftware.InnoSetup" }
    Write-Host "Making the installer..." -ForegroundColor Cyan
    $isccArgs = @("/DAppVersion=$version", "/DRuntime=$Runtime")
    # Inno Setup signs the installer, its helper (setup.tmp, run from the temporary folder) and the uninstaller.
    if ($signCmd) { $isccArgs += @('/DSign=1', ('/Sdeskwheel=$q' + $signCmd + '$q $f')) }
    & $iscc @isccArgs platform\windows\Wheel.iss
    if ($LASTEXITCODE -ne 0) { Write-Error "Installer build failed." }
    $setup = "dist\DeskWheel-$version-$Runtime-setup.exe"
    if ($signCmd -and (Get-AuthenticodeSignature $setup).Status -ne 'Valid') { Write-Error "The installer didn't come out signed." }
    Write-Host "Installer: $setup" -ForegroundColor Green
}
if ($Msix) {
    # ---------- Microsoft Store package ----------
    function Find-SdkTool([string]$name) {
        $arch = if ([Environment]::Is64BitOperatingSystem) { 'x64' } else { 'x86' }
        $tool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\$arch\$name" -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1
        if (-not $tool) { Write-Error "$name not found. Install the Windows SDK: winget install Microsoft.WindowsSDK.10.0.26100" }
        return $tool.FullName
    }
    $makeappx = Find-SdkTool 'makeappx.exe'
    $makepri = Find-SdkTool 'makepri.exe'
    $layout = "dist\msix-$Runtime"
    if (Test-Path $layout) { Remove-Item -Recurse -Force $layout }
    Copy-Item -Recurse "dist\DeskWheel-$Runtime" $layout
    Copy-Item -Recurse platform\windows\msix\Assets "$layout\Assets"
    # Plain names for the manifest; the scaled versions are picked through resources.pri.
    foreach ($n in 'Square44x44Logo', 'Square150x150Logo', 'Wide310x150Logo', 'StoreLogo') {
        Copy-Item "$layout\Assets\$n.scale-100.png" "$layout\Assets\$n.png"
    }
    $msixName = if ($env:WHEEL_MSIX_NAME) { $env:WHEEL_MSIX_NAME } else { 'irfanaslam.DeskWheel' }
    $msixPublisher = if ($env:WHEEL_MSIX_PUBLISHER) { $env:WHEEL_MSIX_PUBLISHER } else { 'CN=DeskWheel Development' }
    $msixPublisherName = if ($env:WHEEL_MSIX_PUBLISHER_NAME) { $env:WHEEL_MSIX_PUBLISHER_NAME } else { 'irfanaslam.io' }
    if (-not $env:WHEEL_MSIX_PUBLISHER) { Write-Warning "WHEEL_MSIX_PUBLISHER isn't set: this package is for local testing; the Store needs your Partner Center identity." }
    $msixArch = if ($Runtime -eq 'win-arm64') { 'arm64' } else { 'x64' }
    $manifest = Get-Content platform\windows\msix\AppxManifest.xml -Raw
    $manifest = $manifest.Replace('{NAME}', $msixName).Replace('{PUBLISHER_NAME}', $msixPublisherName)
    $manifest = $manifest.Replace('{PUBLISHER}', $msixPublisher).Replace('{VERSION}', "$version.0").Replace('{ARCH}', $msixArch)
    Set-Content -Path "$layout\AppxManifest.xml" -Value $manifest -Encoding UTF8

    Push-Location $layout
    try {
        & $makepri createconfig /cf priconfig.xml /dq en-US /o | Out-Null
        & $makepri new /pr . /cf priconfig.xml /of resources.pri /o | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Error "makepri failed." }
        Remove-Item priconfig.xml
    } finally { Pop-Location }

    $package = "dist\DeskWheel-$version-$Runtime.msix"
    & $makeappx pack /d $layout /p $package /o | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Error "makeappx failed." }
    Write-Host "Store package: $package  (upload it in Partner Center; Microsoft signs it)" -ForegroundColor Green
}
} finally {
    if ($signCmd) { Remove-Item -Force $signCmd -ErrorAction SilentlyContinue } # it may hold the certificate password
}

Write-Host ""
Write-Host "Done: dist\DeskWheel-$Runtime\DeskWheel.exe" -ForegroundColor Green
Write-Host "Run it: an orange button appears at the edge of the screen. Drop a file on it, or click it."
