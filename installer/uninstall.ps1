$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$LogFile = Join-Path $root 'uninstall_log.txt'

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
    foreach ($p in $probe) {
        if ($p -and (Test-Path (Join-Path $p 'NuclearOption.exe'))) { return $p }
    }
    return $null
}

Write-Log '=========================================='
Write-Log ' Nuclear Option 简体中文汉化 卸载程序'
Write-Log '=========================================='

$game = Find-GameDir
if (-not $game) {
    Write-Host ''
    Write-Host '未自动找到游戏目录。' -ForegroundColor Yellow
    $manual = Read-Host '请把 Nuclear Option 游戏文件夹拖到这里后按回车'
    $manual = $manual.Trim('"', ' ', "`t")
    if (Test-Path (Join-Path $manual 'NuclearOption.exe')) { $game = $manual }
}
if (-not $game) {
    Write-Log '错误：未找到 Nuclear Option.exe，卸载中止。'
    Read-Host '按回车退出'
    exit 1
}

Write-Log ("游戏目录: " + $game)

$running = Get-Process -Name NuclearOption -ErrorAction SilentlyContinue
if ($running) {
    Write-Log '检测到游戏正在运行，请先退出游戏再卸载。'
    Read-Host '按回车退出'
    exit 1
}

# 1. 删除汉化插件与语言文件
$pluginDir = Join-Path $game 'BepInEx\plugins\NuclearOptionCn'
if (Test-Path $pluginDir) {
    Remove-Item -Path $pluginDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Log '已删除汉化插件与语言文件。'
}

# 2. 若 BepInEx 没有任何其它插件，则整套删除
$bep = Join-Path $game 'BepInEx'
$keepBep = $false
if (Test-Path $bep) {
    $others = @()
    $pluginsDir = Join-Path $bep 'plugins'
    if (Test-Path $pluginsDir) {
        $others = Get-ChildItem -Path $pluginsDir -Force -ErrorAction SilentlyContinue
    }
    if ($others.Count -gt 0) {
        $keepBep = $true
        Write-Log '检测到 BepInEx 下还有其它插件，保留 BepInEx 运行库，仅移除汉化。'
    } else {
        Remove-Item -Path $bep -Recurse -Force -ErrorAction SilentlyContinue
        Write-Log '已删除 BepInEx 目录。'
    }
}

if (-not $keepBep) {
    foreach ($f in @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'nocn_debug.txt', 'nocn_texts.txt', 'changelog.txt')) {
        $p = Join-Path $game $f
        if (Test-Path $p) { Remove-Item -Path $p -Force -ErrorAction SilentlyContinue; Write-Log ('已删除 ' + $f) }
    }
}

Write-Log ''
Write-Log '卸载完成。'
Write-Log '即将调用 Steam 校验游戏文件完整性（会重新下载被修改/缺失的原版文件）。'

# 3. 调用 Steam 校验
try {
    Start-Process 'steam://validate/2168680'
    Write-Log '已向 Steam 发送校验请求，请在 Steam 中确认。'
} catch {
    Write-Log '无法自动调用 Steam，请手动在 Steam 中右键游戏 → 属性 → 已安装文件 → 验证游戏文件完整性。'
}

Read-Host '按回车退出'
