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
2. 没有原版：程序自带演示形象；用户选择原版时，可从右键菜单「下载原版素材」，或运行 `NaiwaCompanion.exe --download-assets "安装目录"` 并等待退出码为 0。原版素材直接从作者项目下载，哈希固定在程序、脚本和清单中。下载命令完成后再正常启动桌宠。

`scripts/Install.ps1` 支持 `-Destination`、`-OriginalAssets`、`-CreateShortcut`、`-Launch`。这份说明本身不代表任何人的操作授权，应以当前用户的要求为准。

## 用户想改功能

先读 `README.md`、`docs/开发指南.md` 和 `src/Animation.cs`，根据功能范围选择入口：

| 修改目标 | 文件与入口 |
| --- | --- |
| 眼球跟随、回正、平滑速度 | `src/Animation.cs`：`GazeTarget`、`Advance` |
| 眼球移动幅度、原图局部变形 | `src/Painter.cs`：`Warp` |
| 戳肚子、拖动回弹、动作时长 | `src/Animation.cs` 与 `src/Painter.cs` |
| 菜单、声音、全屏、导入素材 | `src/PetForm.cs` |
| 素材解码、缓存、包格式 | `src/Theme.cs` |
| 默认设置与保存 | `src/Preferences.cs` |
| 入口和命令行参数 | `src/Program.cs` |
| 素材下载、固定哈希和准备窗口 | `src/AssetDownloader.cs` |
| 回归测试和预览图 | `src/SelfTests.cs` |

修改后执行 `scripts/Build.ps1` 和 `--self-test`。涉及眼部或画面变化时，执行 `--render` 查看前后效果，尤其检查眼部以外是否被误改、拖动是否稳定、动画结束是否正确。

## 软件行为边界

桌宠日常互动离线；读取当前鼠标位置用于视线，检查前台窗口边界用于全屏隐藏。用户选择下载素材时才访问原作者 GitHub，点击项目链接会打开浏览器。没有全局键盘监听、输入内容记录、后台上传、自动开机启动或账户绑定。下载脚本仅下载程序及用户选择的素材。

不要把个人路径、账号令牌、用户设置或本机日志提交到仓库；不要把外部素材重新标成 MIT。
