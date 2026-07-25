# AI接线员 Commander v2.0 - Windows x64 build script
# Run: right-click -> Run with PowerShell

param(
    [string]$Proxy = ""
)

chcp 65001 | Out-Null
Write-Host ""
Write-Host "  ================================" -ForegroundColor Cyan
Write-Host "    AI接线员 Commander v2.0 - Windows Build" -ForegroundColor Cyan
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

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $scriptDir

function Test-NuGetSource {
    param(
        [string]$Url,
        [string]$ProxyUrl
    )

    $request = @{
        Uri = $Url
        Method = "Get"
        UseBasicParsing = $true
        TimeoutSec = 15
        ErrorAction = "Stop"
    }
    if ($ProxyUrl) { $request.Proxy = $ProxyUrl }

    try {
        $response = Invoke-WebRequest @request
        return $response.StatusCode -ge 200 -and $response.StatusCode -lt 400
    } catch {
        return $false
    }
}

function Find-ReachableSources {
    param([string]$ProxyUrl)

    $candidates = @(
        [PSCustomObject]@{ Name = "Huawei Cloud mainland mirror"; Url = "https://repo.huaweicloud.com/repository/nuget/v3/index.json" },
        [PSCustomObject]@{ Name = "nuget.org"; Url = "https://api.nuget.org/v3/index.json" }
    )
    $reachable = @()
    foreach ($candidate in $candidates) {
        Write-Host "  Checking $($candidate.Name) ..." -NoNewline -ForegroundColor Gray
        if (Test-NuGetSource -Url $candidate.Url -ProxyUrl $ProxyUrl) {
            Write-Host " reachable" -ForegroundColor Green
            $reachable += $candidate
        } else {
            Write-Host " unavailable" -ForegroundColor DarkYellow
        }
    }
    return $reachable
}

if ($Proxy) {
    $env:HTTP_PROXY = $Proxy
    $env:HTTPS_PROXY = $Proxy
}

Write-Host "  Checking NuGet connectivity (15-second timeout per source) ..." -ForegroundColor Yellow
$sources = @(Find-ReachableSources -ProxyUrl $Proxy)

if ($sources.Count -eq 0 -and -not $Proxy) {
    Write-Host ""
    Write-Host "  NuGet is unreachable. If a local proxy is running, enter its URL." -ForegroundColor Yellow
    Write-Host "  Example: http://127.0.0.1:7890" -ForegroundColor Gray
    $Proxy = Read-Host "  Proxy URL (press Enter to cancel)"
    if ($Proxy) {
        $env:HTTP_PROXY = $Proxy
        $env:HTTPS_PROXY = $Proxy
        $sources = @(Find-ReachableSources -ProxyUrl $Proxy)
    }
}

if ($sources.Count -eq 0) {
    Write-Host ""
    Write-Host "  [ERROR] No NuGet package source is reachable." -ForegroundColor Red
    Write-Host "  Check the network, system clock, TLS certificate, VPN or proxy, then retry." -ForegroundColor Gray
    Pop-Location
    Write-Host "  Press any key to exit..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 1
}

# Restore one source at a time. NuGet otherwise queries all configured sources in parallel,
# so one unhealthy mirror can make the build appear stuck at "Determining projects to restore".
$restoreSucceeded = $false
foreach ($source in $sources) {
    Write-Host ""
    Write-Host "  Restoring packages from $($source.Name) ..." -ForegroundColor Yellow
    dotnet restore "$scriptDir\Commander.csproj" -r win-x64 `
        --source $($source.Url) `
        --disable-parallel `
        --verbosity normal `
        --tl:off
    if ($LASTEXITCODE -eq 0) {
        $restoreSucceeded = $true
        break
    }
    Write-Host "  Restore failed from $($source.Name); trying the next reachable source." -ForegroundColor DarkYellow
}

if (-not $restoreSucceeded) {
    Write-Host ""
    Write-Host "  [ERROR] NuGet package restore failed." -ForegroundColor Red
    Write-Host "  The messages above now show the exact source or package that failed." -ForegroundColor Gray
    Pop-Location
    Write-Host "  Press any key to exit..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 1
}

# --- 打包前清场 ---------------------------------------------------------
# 单文件打包（GenerateBundle）要覆写 publish\Commander.exe。若上一版还在运行，
# 或文件被杀软/资源管理器占着，会抛 IOException 变成 MSB4018——编译其实是成功的，
# 只是最后一步写不进去。这里先关掉旧进程再打包。
$running = Get-Process -Name "Commander" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "  Closing running Commander.exe ($($running.Count)) ..." -ForegroundColor Yellow
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
}

# 目标 exe 仍被占用时明确报出来，别让用户对着 MSB4018 堆栈猜
$targetExe = Join-Path $scriptDir "publish\Commander.exe"
if (Test-Path $targetExe) {
    try {
        $stream = [System.IO.File]::Open($targetExe, 'Open', 'ReadWrite', 'None')
        $stream.Close()
    } catch {
        Write-Host ""
        Write-Host "  [ERROR] publish\Commander.exe is locked by another process." -ForegroundColor Red
        Write-Host "  关掉正在运行的 AI接线员（含托盘图标）、关掉打开该目录的资源管理器窗口，或把杀软的实时扫描排除该目录后重试。" -ForegroundColor Gray
        Pop-Location
        Write-Host "  Press any key to exit..."
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        exit 1
    }
}

# 网络路径（UNC / 映射盘）上做单文件打包容易踩 SMB 文件锁和延迟写入，强烈建议本地盘构建
if ($scriptDir -like "\\*") {
    Write-Host ""
    Write-Host "  [WARN] 正在从网络路径构建：$scriptDir" -ForegroundColor Yellow
    Write-Host "         SMB 共享的文件锁与写入延迟会让单文件打包偶发失败（GenerateBundle / MSB4018）。" -ForegroundColor Gray
    Write-Host "         建议先复制到本地盘再构建，例如：robocopy `"$scriptDir`" C:\dev\Commander-Windows /E" -ForegroundColor Gray
    Write-Host ""
}

Write-Host ""
Write-Host "  Building Commander.exe ..." -ForegroundColor Yellow
Write-Host ""
dotnet publish "$scriptDir\Commander.csproj" -c Release -r win-x64 --self-contained true `
    --no-restore `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o "$scriptDir\publish"

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "  ================================" -ForegroundColor Green
    Write-Host "    Build successful!" -ForegroundColor Green
    Write-Host "    Output: publish\Commander.exe (self-contained)" -ForegroundColor Green
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
