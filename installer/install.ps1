$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$LogFile = Join-Path $root 'install_log.txt'

function Write-Log($msg) {
    Write-Host $msg
    try { Add-Content -Path $LogFile -Value ("[" + (Get-Date -Format 'HH:mm:ss') + "] " + $msg) -Encoding UTF8 } catch {}
}

function Get-SteamLibraries {
    $libs = @()
    $steam = $null
    foreach ($k in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam')) {
        try {
            $p = (Get-ItemProperty -Path $k -ErrorAction Stop)
            if ($p.SteamPath) { $steam = $p.SteamPath; break }
            if ($p.InstallPath) { $steam = $p.InstallPath; break }
        } catch {}
    }
    if ($steam) {
        $libs += $steam
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            $txt = Get-Content $vdf -Raw -Encoding UTF8
            foreach ($m in [regex]::Matches($txt, '"path"\s+"([^"]+)"')) {
                $p = $m.Groups[1].Value -replace '\\\\', '\'
                if ($libs -notcontains $p) { $libs += $p }
            }
        }
    }
    return $libs
}

function Find-GameDir {
    $probe = @()
    if (Test-Path (Join-Path $root 'NuclearOption.exe')) { return $root }
    $parent = Split-Path -Parent $root
    $probe += (Join-Path $parent 'Nuclear Option')
    $probe += $parent
    foreach ($lib in Get-SteamLibraries) {
        $probe += (Join-Path $lib 'steamapps\common\Nuclear Option')
    }
    $probe += 'C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option'
    $probe += 'D:\SteamLibrary\steamapps\common\Nuclear Option'
    $probe += 'E:\SteamLibrary\steamapps\common\Nuclear Option'
    $probe += 'F:\SteamLibrary\steamapps\common\Nuclear Option'
    $probe += 'G:\SteamLibrary\steamapps\common\Nuclear Option'
    foreach ($p in $probe) {
        if ($p -and (Test-Path (Join-Path $p 'NuclearOption.exe'))) { return $p }
    }
    return $null
}

Write-Log '=========================================='
Write-Log ' Nuclear Option 简体中文汉化 安装程序'
Write-Log '=========================================='

$game = Find-GameDir
if (-not $game) {
    Write-Host ''
    Write-Host '未自动找到游戏目录。' -ForegroundColor Yellow
    $manual = Read-Host '请把 Nuclear Option 游戏文件夹拖到这里后按回车（或直接输入路径）'
    $manual = $manual.Trim('"', ' ', "`t")
    if (Test-Path (Join-Path $manual 'NuclearOption.exe')) { $game = $manual }
}
if (-not $game) {
    Write-Log '错误：未找到 Nuclear Option.exe，安装中止。'
    Read-Host '按回车退出'
    exit 1
}

Write-Log ("游戏目录: " + $game)

$running = Get-Process -Name NuclearOption -ErrorAction SilentlyContinue
if ($running) {
    Write-Log '检测到游戏正在运行，请先退出游戏再安装。'
    Read-Host '按回车退出'
    exit 1
}

$src = Join-Path $root 'files'
if (-not (Test-Path $src)) {
    Write-Log '错误：安装包不完整（缺少 files 目录）。'
    Read-Host '按回车退出'
    exit 1
}

Write-Log '正在复制文件……'
try {
    Copy-Item -Path (Join-Path $src '*') -Destination $game -Recurse -Force
} catch {
    Write-Log ('复制失败: ' + $_.Exception.Message)
    Read-Host '按回车退出'
    exit 1
}

# 校验关键文件
$checks = @(
    'winhttp.dll',
    'doorstop_config.ini',
    'BepInEx\core\BepInEx.Preloader.dll',
    'BepInEx\plugins\NuclearOptionCn\NuclearOptionCn.dll',
    'BepInEx\plugins\NuclearOptionCn\zh_cn.tsv'
)
$ok = $true
foreach ($c in $checks) {
    if (-not (Test-Path (Join-Path $game $c))) { Write-Log ('缺少文件: ' + $c); $ok = $false }
}

if ($ok) {
    Write-Log ''
    Write-Log '安装完成！'
    Write-Log '直接启动游戏即可，界面会自动显示中文。'
    Write-Log '卸载请运行 uninstall.bat。'
} else {
    Write-Log '安装似乎不完整，请检查上面的错误。'
}
Read-Host '按回车退出'
