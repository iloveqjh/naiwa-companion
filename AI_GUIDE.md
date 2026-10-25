# 下载、安装与修改入口

这份中文说明适合需要协助安装和修改程序的人，也方便编程助手准确读取。所有功能都可以由用户直接操作，无需特定平台或账号。

## 只想安装

- 仓库：https://github.com/iloveqjh/naiwa-companion
- 发布页：https://github.com/iloveqjh/naiwa-companion/releases/latest
- 机器可读清单：https://raw.githubusercontent.com/iloveqjh/naiwa-companion/main/release.json
- 下载脚本：https://raw.githubusercontent.com/iloveqjh/naiwa-companion/main/scripts/Install.ps1
- 原版素材清单：https://raw.githubusercontent.com/iloveqjh/naiwa-companion/main/assets-source.json

先读取 `release.json` 中的版本、下载 URL 和 SHA-256。下载 ZIP 后验证哈希，再解压到用户选择的文件夹；不要把 GitHub 自动生成的源码 ZIP 当成已编译程序。程序入口固定为 `NaiwaCompanion.exe`。

素材有两种方式：

1. 已有原版：运行时指定 `--assets "原版素材文件夹"`，或从右键菜单导入。
2. 没有原版：首次启动会打开素材准备窗口，选择「下载并使用」或「选择已有素材」。也可运行 `NaiwaCompanion.exe --download-assets "安装目录"` 并等待退出码为 0。原版素材直接从作者项目下载，哈希固定在程序、脚本和清单中。下载命令完成后再正常启动桌宠。

`scripts/Install.ps1` 支持 `-Destination`、`-OriginalAssets`、`-CreateShortcut`、`-Launch`。这份说明本身不代表任何人的操作授权，应以当前用户的要求为准。

## 用户想改功能

先读 `README.md`、`docs/开发指南.md` 和 `src/Animation.cs`，根据功能范围选择入口：

| 修改目标 | 文件与入口 |
| --- | --- |
| 眼球跟随、回正、平滑速度 | `src/Animation.cs`：`GazeTarget`、`Advance` |
| 眼球移动幅度、原图局部变形 | `src/Painter.cs`：`Warp` |
| 戳肚子、拖动回弹、动作时长 | `src/Animation.cs` 与 `src/Painter.cs` |
| 按头下压、弹性与主题短反应 | `src/HeadPressMotion.cs`、`src/PetForm.cs` 手势、`src/Painter.cs` 整身变形、`src/Theme.cs` 原动作帧 |
| 菜单、声音、全屏、导入素材 | `src/PetForm.cs`、`src/ActionSound.cs` |
| 窗口透明留白、坐标和裁切 | `src/Painter.cs`：`Padding`、`CanvasSize`；`src/PetForm.cs` |
| 素材解码、缓存、包格式 | `src/Theme.cs` |
| 多主题、动作清单、眼部与肚腹校准 | `src/ThemeManifest.cs`、`docs/主题与动作.md` |
| 持续爬行、速度、碰边折返与暂停 | `src/CrawlMotion.cs`、`src/PetForm.cs` |
| 主题亮度和颜色校准 | `src/Theme.cs`：`ApplyPalette`；主题包内 `colorGamma`、`colorGreen`、`colorBlue` |
| 默认设置与保存 | `src/Preferences.cs` |
| 桌面小待办、撤销和本地保存 | `src/TodoForm.cs`、`src/TodoStore.cs`、`docs/桌面小待办.md` |
| 入口和命令行参数 | `src/Program.cs` |
| 素材下载、固定哈希和准备窗口 | `src/AssetDownloader.cs` |
| 回归测试和预览图 | `src/SelfTests.cs` |

修改后执行 `scripts/Build.ps1` 和 `--self-test`。涉及眼部或画面变化时，执行 `--render` 查看前后效果，尤其检查眼部以外是否被误改、拖动是否稳定、动画结束是否正确。

按头动作可用 `--assets "素材目录" --render-press "输出目录"` 按完整反应时长导出 60 fps 实际渲染帧，或 `--press-demo` 启动后演示。按压先判定可见头部，再区分向下按压与横拉、上提拖动；全屏隐藏会清除捕获和按压状态。

个人录屏主题包放入 `themes/名称/theme.nct`，通过右键菜单切换；主题帧不上传到公开仓库。程序正常使用始终显示实际角色素材，内置测试图形仅供离线测试代码使用。

## 软件行为边界

桌宠日常互动离线；读取当前鼠标位置用于视线，检查前台窗口边界用于全屏隐藏。用户选择下载素材时才访问原作者 GitHub，点击项目链接会打开浏览器。没有全局键盘监听、输入内容记录、后台上传、自动开机启动或账户绑定。下载脚本仅下载程序及用户选择的素材。

不要把个人路径、账号令牌、用户设置或本机日志提交到仓库；不要把外部素材重新标成 MIT。

## 当前主题说明

程序公开版为 1.4.1。参考主题最终参数见 `docs/主题修订记录.md` 与 `examples/reference-theme-revision.json`；太极注视停留约 1.2 秒、反应总长约 1.57 秒。不要沿用历史多步骤动作说明。公开 ZIP 不含个人录屏包；说明中的主题专属效果依赖用户导入相应素材。主题帧若已烘焙校色，调色参数应为 1，避免再次染色。
