# Builds DualSense Precision and packages it into a Setup.exe.
# Easiest way to run it: double-click build-installer.cmd (it runs this script for you).

param(
    # Used by the GitHub build: don't open File Explorer at the end.
    [switch]$NoExplorer
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Step($text) { Write-Host ""; Write-Host "== $text" -ForegroundColor Cyan }

# Version comes from Directory.Build.props so the app, the installer and the file name always match.
$props = [xml](Get-Content (Join-Path $PSScriptRoot 'Directory.Build.props'))
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw "Couldn't read <Version> from Directory.Build.props." }
Write-Host "DualSense Precision $version" -ForegroundColor Green

# 1. Inno Setup must be installed (it's what turns the build into a Setup.exe).
$isccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe"
)
function Find-Iscc {
    $found = $isccCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $found) {
        $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($cmd) { $found = $cmd.Source }
    }
    return $found
}

$iscc = Find-Iscc
if (-not $iscc) {
    Step "Inno Setup isn't installed - installing it now (one time only)"
    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $winget) {
        Write-Host "winget isn't available on this PC, so Inno Setup can't be installed automatically." -ForegroundColor Yellow
        Write-Host "Download and install it from https://jrsoftware.org/isdl.php, then run this again." -ForegroundColor Yellow
        exit 1
    }
    Write-Host "Running: winget install JRSoftware.InnoSetup"
    Write-Host "If Windows asks for permission, click Yes." -ForegroundColor Yellow
    & winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements
    $iscc = Find-Iscc
    if (-not $iscc) {
        Write-Host ""
        Write-Host "Inno Setup still can't be found after installing. Close this window and run build-installer again;" -ForegroundColor Yellow
        Write-Host "if it keeps happening, send Claude everything above." -ForegroundColor Yellow
        exit 1
    }
    Write-Host "Inno Setup installed: $iscc" -ForegroundColor Green
}

# 2. Tests first: never package a build that fails them.
Step "Running tests"
dotnet test tests\DualSensePrecision.Tests -c Release
if ($LASTEXITCODE -ne 0) { throw "Tests failed - installer not built." }

# 3. Release build, self-contained: users don't need .NET or the Windows App SDK installed.
Step "Publishing the app (Release, self-contained, x64)"
if (Test-Path publish) { Remove-Item publish -Recurse -Force }
dotnet publish src\DualSensePrecision.App\DualSensePrecision.App.csproj `
    -c Release -r win-x64 --self-contained true -p:Platform=x64 -o publish\win-x64
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
if (-not (Test-Path publish\win-x64\DualSensePrecision.exe)) { throw "Publish finished but DualSensePrecision.exe is missing." }

# 4. Package.
Step "Building the installer"
& $iscc "/DAppVersion=$version" installer\DualSensePrecision.iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }

$setup = Join-Path $PSScriptRoot "installer\Output\DualSensePrecision-$version-setup.exe"
Step "Done"
Write-Host "Installer: $setup" -ForegroundColor Green
if (-not $NoExplorer) { Start-Process explorer.exe "/select,`"$setup`"" }
