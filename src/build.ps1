param(
    [string]$GameDir = $env:NUCLEAR_OPTION_DIR,
    [string]$OutDir  = (Join-Path (Split-Path -Parent $PSScriptRoot) 'build'),
    [string]$Dotnet  = 'dotnet',
    [string]$CscDll  = ''
)

$ErrorActionPreference = 'Stop'

if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir 'NuclearOption_Data\Managed'))) {
    throw "请用 -GameDir 指定游戏根目录（包含 NuclearOption_Data 的那层），或设置环境变量 NUCLEAR_OPTION_DIR"
}

$managed = Join-Path $GameDir 'NuclearOption_Data\Managed'
$src     = Join-Path $PSScriptRoot 'CnPlugin.cs'
if (-not (Test-Path $src)) { throw "找不到源文件: $src" }

# 引用：Unity 程序集 + 游戏程序集 + BepInEx/Harmony
$refs = @(
    'mscorlib.dll', 'System.dll', 'System.Core.dll', 'netstandard.dll',
    'UnityEngine.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.UI.dll',
    'UnityEngine.UIModule.dll', 'UnityEngine.TextRenderingModule.dll',
    'UnityEngine.InputLegacyModule.dll', 'UnityEngine.TextCoreFontEngineModule.dll',
    'UnityEngine.TextCoreTextEngineModule.dll', 'UnityEngine.IMGUIModule.dll',
    'Unity.TextMeshPro.dll', 'Assembly-CSharp.dll'
) | ForEach-Object { Join-Path $managed $_ } | Where-Object { Test-Path $_ }

$plugins = Join-Path $GameDir 'BepInEx\plugins\NuclearOptionCn'
$bepCore = Join-Path $GameDir 'BepInEx\core'
foreach ($d in @($bepCore, $plugins)) {
    foreach ($n in @('BepInEx.dll', '0Harmony.dll')) {
        $p = Join-Path $d $n
        if (Test-Path $p) { $refs += $p }
    }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$rsp = Join-Path $OutDir 'build.rsp'
$lines = @(
    '/nologo', '/target:library', '/langversion:7.3', '/optimize+', '/unsafe-',
    ('/out:"' + (Join-Path $OutDir 'NuclearOptionCn.dll') + '"'),
    ('"' + $src + '"')
)
foreach ($r in $refs) { $lines += ('/r:"' + $r + '"') }
Set-Content -Path $rsp -Value $lines -Encoding ASCII

if (-not $CscDll) {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'dotnet\sdk'),
        (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\sdk')
    )
    foreach ($c in $candidates) {
        if (-not (Test-Path $c)) { continue }
        $hit = Get-ChildItem $c -Recurse -Filter csc.dll -ErrorAction SilentlyContinue |
               Where-Object { $_.FullName -match 'Roslyn\\bincore' } | Select-Object -First 1
        if ($hit) { $CscDll = $hit.FullName; break }
    }
}
if (-not $CscDll) { throw "找不到 csc.dll。请安装 .NET SDK，或用 -CscDll 指定 Roslyn 编译器路径" }

& $Dotnet $CscDll ('@' + $rsp)
if ($LASTEXITCODE -ne 0) { throw "编译失败 (exit $LASTEXITCODE)" }
Write-Host ("OK -> " + (Join-Path $OutDir 'NuclearOptionCn.dll'))
