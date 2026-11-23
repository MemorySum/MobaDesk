<#
.SYNOPSIS
    Builds a release package of MobaDesk.

.DESCRIPTION
    Modes:
      (default)  Self-contained, slim payload. No prerequisites on the target machine.
      -Fx        Framework-dependent. Needs .NET 8 Desktop Runtime + Windows App Runtime 1.8
                 installed on the target machine; output is much smaller.
      -R2R       Adds ReadyToRun precompilation (faster startup, larger output).
      -Aot       Native AOT. Requires the MSVC linker (Visual Studio "Desktop development with
                 C++" workload). Windows App SDK / WinUI does not officially support Native AOT
                 yet, so treat this as experimental.

.EXAMPLE
    .\publish.ps1                 # self-contained slim build in publish\win-x64
    .\publish.ps1 -Fx -Out publish\win-x64-fx
#>
param(
    [switch]$Fx,
    [switch]$R2R,
    [switch]$Aot,
    [switch]$Force,
    [string]$Out,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\MobaDesk\MobaDesk.csproj'
if (-not (Test-Path $project)) {
    throw "Cannot find $project"
}

function Find-MsvcLinker {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $paths = & $vswhere -latest -products * `
            -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
            -find 'VC\Tools\MSVC\**\bin\Hostx64\x64\link.exe' 2>$null
        if ($paths) { return ($paths | Select-Object -First 1) }
    }

    $onPath = Get-Command link.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    return $null
}

if ($Aot -and -not $Force) {
    $linker = Find-MsvcLinker
    if (-not $linker) {
        Write-Host ''
        Write-Host '无法进行 Native AOT 发布：找不到 MSVC 链接器 (link.exe)' -ForegroundColor Yellow
        Write-Host ''
        Write-Host 'ILC 需要「使用 C++ 的桌面开发」工作负载提供的链接器，安装方式任选其一：'
        Write-Host '  1) 安装 Visual Studio 生成工具（推荐）'
        Write-Host '     winget install --id Microsoft.VisualStudio.2022.BuildTools -e'
        Write-Host '     然后在安装器中勾选「使用 C++ 的桌面开发」'
        Write-Host '  2) 已安装 Visual Studio 时，追加该工作负载即可'
        Write-Host ''
        Write-Host '另外请注意：Windows App SDK / WinUI 3 目前官方不支持 Native AOT，即使具备链接器，'
        Write-Host '该模式仍属实验性质（可能出现 XAML 或 WinRT 运行时异常）。'
        Write-Host ''
        Write-Host '要跳过此检查并尝试链接，请加上 -Force。' -ForegroundColor Yellow
        Write-Host ''
        exit 1
    }

    Write-Host "MSVC linker: $linker" -ForegroundColor DarkGray

    # ILC's findvcvarsall.bat chain calls `vswhere.exe` unqualified; make sure it resolves.
    $vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
    if (Test-Path (Join-Path $vsInstaller 'vswhere.exe')) {
        $env:PATH = "$vsInstaller;$env:PATH"
    }
}

if (-not $Out) {
    $Out = if ($Fx) { 'publish\win-x64-fx' } elseif ($Aot) { 'publish\win-x64-aot' } else { 'publish\win-x64' }
}

$dotnetArgs = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', 'win-x64',
    '-o', (Join-Path $PSScriptRoot $Out)
)

if ($Fx) {
    $dotnetArgs += @('--self-contained', 'false', '-p:WindowsAppSDKSelfContained=false')
}
else {
    $dotnetArgs += @('--self-contained', 'true')
}

if ($R2R) { $dotnetArgs += '-p:MobaDeskReadyToRun=true' }
if ($Aot) { $dotnetArgs += '-p:MobaDeskNativeAot=true' }

Write-Host "publish: $($dotnetArgs -join ' ')" -ForegroundColor Cyan
& dotnet @dotnetArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$target = Join-Path $PSScriptRoot $Out
$stat = Get-ChildItem $target -Recurse -File | Measure-Object -Property Length -Sum
Write-Host ("published {0} -> {1:N1} MB in {2} files" -f $Out, ($stat.Sum / 1MB), $stat.Count) -ForegroundColor Green

if ($Aot) {
    Write-Host ''
    Write-Host '注意：AOT 产物已经链接成功，但 Windows App SDK / WinUI 3 目前不支持 Native AOT。' -ForegroundColor Yellow
    Write-Host '实测启动会崩溃（CsWinRT 把托管集合封送给 ItemsControl.set_ItemsSource 时需要运行时代码生成）：' -ForegroundColor Yellow
    Write-Host '  System.ArgumentException: Arg_ArgumentException' -ForegroundColor DarkGray
    Write-Host '    at ABI.Microsoft.UI.Xaml.Controls.IItemsControlMethods.set_ItemsSource(...)' -ForegroundColor DarkGray
    Write-Host '    at MobaDesk.MainWindow..ctor()' -ForegroundColor DarkGray
    Write-Host '仅在生产环境具备官方支持后再启用该模式；日常发布请使用默认模式。' -ForegroundColor Yellow
    Write-Host ''
}

