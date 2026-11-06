# Nuclear Option 简体中文汉化

《Nuclear Option（核选项）》的完整简体中文汉化补丁。

> 作者：**BBL 和他的蓝色大肥鱼**

- 适用游戏版本：**Nuclear Option 0.34.6（Steam）**
- 当前汉化版本：**v1.3.0**
- 词表规模：**8,213 条**
- 不修改任何游戏原始文件；卸载 = 删除插件 + Steam 校验完整性

---

## 为什么做这个

社区里 2024 年那版汉化有两个老毛病：一是靠运行时逐帧扫描 + 正则 + 联网机翻，
在菜单和大地图里明显掉帧；二是字体整包替换，观感和原版偏差大。

这个项目换了个思路：**离线词典 + 精确匹配 + 零网络 + 不动字体贴图**。

| | 旧方案（AutoTranslator 类） | 本项目 |
|---|---|---|
| 翻译来源 | 联网机翻，未命中就等网络 | 本地词典，永不联网 |
| 匹配方式 | 每帧/每场景全量扫描 + 逐串正则 | 文本赋值时查一次字典 |
| 单次开销 | 毫秒级且随场景增长 | 命中 0.07µs / 未命中 1.8µs（插件内实测） |
| 字体 | 整包替换字体资源 | 保留原版拉丁字体，中文按需回退渲染 |
| 兼容性 | 改游戏资源 | 不改任何原始文件 |

---

## 安装

1. 下载 [Releases](../../releases) 里的 `NuclearOption-CN-vX.Y.Z.zip`
2. 解压到任意位置
3. 双击 **`install.bat`**
   - 会自动从注册表 / `libraryfolders.vdf` 找游戏目录
   - 找不到时会提示你把游戏文件夹拖进窗口
4. 启动游戏，界面即为中文

> 杀毒软件可能对 `winhttp.dll` 报警：那是 BepInEx 的注入器，属正常工具，加白名单即可。

## 卸载

双击 **`uninstall.bat`**：

- 删除汉化插件与语言文件
- 若 BepInEx 下没有其它插件，连 BepInEx 一并清理
- 自动调用 `steam://validate/<appid>` 验证游戏完整性，清掉可能的残留

## 使用

| 操作 | 说明 |
|---|---|
| `F10` | 重新读取 `zh_cn.tsv`（改词表不用重启） |
| `config.ini` | 开关汉化、字体模式、调试选项 |

配置文件：`BepInEx\plugins\NuclearOptionCn\config.ini`

```ini
enable    = true       # 关掉汉化但保留安装
font_mode = fallback   # fallback / translated / unified
sweep     = false      # 补译扫描，默认关闭（见下）
debug_fps = false
```

关于 `sweep`：它会遍历场景里所有文本组件做补译。**实测开启后会在主菜单卡顿，
极端情况下导致游戏退出**，所以默认关闭。绝大多数文本由 `.text` / `SetText` /
`OnEnable` 三个补丁覆盖，通常不需要它。

---

## 覆盖范围

- 主菜单、设置（控制 / 游戏性 / 音频 / 画面 / HUD / 社交 / 辅助功能 + 按键绑定）
- 百科：飞机 / 载具 / 舰船 / 建筑 / 导弹 全部条目、参数面板、说明文本
- 任务编辑器：参数 / 环境 / 阵营 / 限制 等全部面板
- 任务选择：任务名、任务说明、筛选标签
- 多人服务器列表、出击界面、挂载与涂装
- 任务内 HUD、告警播报、教程对话、任务脚本消息
- 全部 98 条「你知道吗」小知识

### 翻译约定

- **单位俗名音译**：亥伯龙级、阿格斯级、迪纳摩级、安纳克斯级、库尔索级、沙德级、冲浪级
- **机型绰号意译**：蟋蟀、罗盘、狼蛛、美杜莎、急弯、伊夫利特、斗士、镰刀、弯刀、
  枢纽、主销、铁砧、剥夺者、后坐、螺旋钻、朱鹮、折刀、雷击、流浪者、精灵、眼球、暗域、涡流、翠鸟
- **保留原文**：BDF / PALA 等阵营缩写、武器型号前缀（AGM-99、AAM-29）、
  内部标识符、玩家名、服务器名、工坊涂装作者名、硬件产品名

---

## 构建

需要：.NET SDK（或仅 Roslyn 编译器 `csc.dll`）+ 游戏 `Managed` 目录下的程序集。

```powershell
# 1. 放入游戏程序集引用（默认从环境变量 NUCLEAR_OPTION_DIR 或 -GameDir 读取）
# 2. 编译
.\src\build.ps1 -GameDir "G:\SteamLibrary\steamapps\common\Nuclear Option"

# 3. 产物
#    build\NuclearOptionCn.dll
```

编译依赖：`UnityEngine*.dll`、`Unity.TextMeshPro.dll`、`Assembly-CSharp.dll`、
`BepInEx.dll`、`0Harmony.dll`。

---

## 已知限制

- 任务缩略图里烙死的英文标题（`missionimages/*.png`）无法翻译
- 部分纯内部标识符刻意不译（如 `Aircraft.RpcEscapeCapsule`）
- 摇杆/头瞄等硬件产品名保留原文

---

## 许可

代码以 **MIT** 发布（作者：BBL 和他的蓝色大肥鱼），词表（`translation/zh_cn.tsv`）可自由取用、修改、再分发。

运行依赖 [BepInEx](https://github.com/BepInEx/BepInEx) 5.4.23.6（LGPL-2.1）与 HarmonyX。
游戏本体版权归 Shockfront Studios 所有，本项目与官方无关。
