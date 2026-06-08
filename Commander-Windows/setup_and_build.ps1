# 接线员 Commander - Build script
# Run: right-click -> Run with PowerShell

chcp 65001 | Out-Null
Write-Host ""
Write-Host "  ================================" -ForegroundColor Cyan
Write-Host "    接线员 Commander - Windows Build" -ForegroundColor Cyan
Write-Host "  ================================" -ForegroundColor Cyan
Write-Host ""

# Check if dotnet SDK is installed
$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCmd) {
    # Also check common install paths
    $localDotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
    $progDotnet = "$env:ProgramFiles\dotnet\dotnet.exe"

    if (Test-Path $localDotnet) {
        $env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
    } elseif (Test-Path $progDotnet) {
        $env:PATH = "$env:ProgramFiles\dotnet;$env:PATH"
    } else {
        Write-Host "  [!] .NET 8 SDK not found." -ForegroundColor Red
        Write-Host ""
        Write-Host "  Please install .NET 8 SDK first:" -ForegroundColor Yellow
        Write-Host "  https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor White
        Write-Host ""
        Write-Host "  Download the SDK installer (Windows x64), run it," -ForegroundColor Gray
        Write-Host "  then run this script again." -ForegroundColor Gray
        Write-Host ""

        # Open download page
        $openPage = Read-Host "  Open download page in browser? (Y/n)"
        if ($openPage -ne 'n' -and $openPage -ne 'N') {
            Start-Process "https://dotnet.microsoft.com/download/dotnet/8.0"
        }

        Write-Host ""
        Write-Host "  Press any key to exit..."
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        exit 1
    }
}

$sdkVersion = & dotnet --version 2>$null
Write-Host "  [OK] .NET SDK: $sdkVersion" -ForegroundColor Green
Write-Host ""

# Build
Write-Host "  Building Commander.exe ..." -ForegroundColor Yellow
Write-Host ""
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $scriptDir

dotnet publish -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o "$scriptDir\publish"

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "  ================================" -ForegroundColor Green
    Write-Host "    Build successful!" -ForegroundColor Green
    Write-Host "    Output: publish\Commander.exe" -ForegroundColor Green
    Write-Host "  ================================" -ForegroundColor Green
    Write-Host ""
    explorer "$scriptDir\publish"
} else {
    Write-Host ""
    Write-Host "  [ERROR] Build failed!" -ForegroundColor Red
}

Pop-Location
Write-Host "  Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
