# 奶蛙陪伴 · Naiwa Companion

**鼠标在身外，眼睛轻柔跟随；鼠标移到身上，视线缓缓回正。**

一个轻量的 Windows 桌宠：透明悬浮、自然眨眼、戳肚子回弹、拖动倾斜、双击大笑、笑倒后停留。兼容原版奶蛙素材包，保留原图与原动作的质感。程序使用 C# / .NET Framework 4.x，Windows 10 / 11 可直接运行。

**[下载 Windows 版](https://github.com/iloveqjh/naiwa-companion/releases/latest) · [中文使用指南](docs/使用指南.md) · [下载与修改入口](AI_GUIDE.md) · [开发指南](docs/开发指南.md)**

## 三步开始

1. 在下载页找到 `NaiwaCompanion-Windows-v1.1.0.zip`，解压到喜欢的位置，例如 `D:\NaiwaCompanion`。
2. 双击 `NaiwaCompanion.exe`，使用原版立体奶蛙素材。新版本首次缺少素材时会打开素材准备窗口。
3. 想使用原版奶蛙：右键桌宠 →「下载原版素材」→「下载并使用」，完成后自动切换。已有素材可右键导入；也可从 [原作者项目](https://github.com/yugh6/Naiwa) 下载 `素材.nwp`、`原版笑声.wav` 放到程序旁边。

已经装好原版的用户无需重新下载素材，在新程序的右键菜单里选择「导入原版素材」，选中原来的文件夹即可。

## 新增主题与动作

1.1 版支持透明序列帧主题包：将 `theme.nct` 放入程序旁的 `themes/主题名/` 文件夹，右键「切换主题」即可选择。右键「选择动作」可播放主题中的不同动作，也可开启「循环当前动作」。循环之间会短暂停留，动作结束通过过渡恢复待机。

新主题保持原素材的立体外观，支持各自的眼部和肚腹区域校准。双击播放当前选中的动作；切回经典主题后保留原版完整大笑。格式和制作方法见 [主题与动作](docs/主题与动作.md)。

外部角色和录屏素材包由用户自行导入，不包含在公开发布包中。程序缺少角色素材时会打开素材准备窗口，不会显示简笔替代形象。

## 怎么玩

| 操作 | 反应 |
| --- | --- |
| 鼠标在身体外移动 | 眼睛轻微跟随，上下左右都有效，距离越远逐渐达到自然上限 |
| 鼠标移到身体上 | 视线缓缓回正；移开后继续跟随 |
| 不操作 | 随机间隔眨眼，静静陪伴 |
| 单击肚子 | 原图肚腹局部压下、回弹，脸部与脚部保持稳定 |
| 双击 | 播放完整大笑动作；原版素材约 14 秒 |
| 笑倒后单击 | 恢复站立 |
| 拖动、松手 | 拎起时轻微倾斜，松手时回弹；位置会保存 |
| 右键 | 设置大小、声音、跟随、置顶、全屏隐藏，导入素材或退出 |
| 再次运行程序 | 唤回同一只桌宠，移到鼠标所在屏幕右下角 |

声音默认关闭；开启后从下一次大笑开始同步播放。前台全屏应用位于奶蛙所在屏幕时，奶蛙自动隐藏并停止笑声；切回来后恢复。

## 改进了什么

- 视线从简单悬停切换改为连续的二维鼠标跟随，并提供「身上回正」行为。
- 用原眼部纹理做小范围形变，保留角色外观；眨眼与大笑期间优先保留原动作。
- 按实际经过时间播放动作，定时器偶尔延迟时不会让大笑越播越慢。
- 静止时减少刷新；动作帧按需解码，最多缓存 16 张，避免常驻数百张大图。
- 提供可直接运行的程序、文件校验、中文教程、可执行测试和结构清楚的修改入口。

完整分析见 [原版研究与改进](docs/原版研究与改进.md)。

## 外观以原版立体奶蛙为准

使用原版素材保留角色的皮肤、身体比例和动作质感。新主题采用透明序列帧动作包，保持素材本身的外观。

![原版奶蛙的立体形象，图片来自素材作者](https://raw.githubusercontent.com/yugh6/Naiwa/553afb87508d3b05cda3af935616ed00e94dd3e4/%E5%BD%A2%E8%B1%A1%E9%A2%84%E8%A7%88.png)

## 自动下载与安装

面向需要协助安装或修改程序的用户，统一入口是 [AI_GUIDE.md](AI_GUIDE.md)。下载清单为 [release.json](release.json)，素材来源清单为 [assets-source.json](assets-source.json)。不需要注册、邀请码或 API Key。

也可以先下载 [Install.ps1](https://raw.githubusercontent.com/iloveqjh/naiwa-companion/main/scripts/Install.ps1)，确认内容后在 PowerShell 中运行：

```powershell
.\Install.ps1 -Destination 'D:\NaiwaCompanion' -OriginalAssets -CreateShortcut -Launch
```

脚本下载并校验发布包；选择 `-OriginalAssets` 时，再从原作者仓库获取素材。脚本不会修改系统执行策略。若电脑限制运行脚本，使用下载页的 ZIP 和手动放置素材方式即可。

## 从源码构建

```powershell
.\scripts\Build.ps1
$test = Start-Process '.\dist\NaiwaCompanion.exe' -ArgumentList '--self-test test-output' -Wait -PassThru
Get-Content '.\test-output\results.txt'
if ($test.ExitCode -ne 0) { throw '测试失败' }
```

如需检验原版素材，在测试参数前加 `--assets "素材所在文件夹"`。运行环境、参数和素材格式见 [开发指南](docs/开发指南.md)。

## 开源与来源

程序源码、中文文档和内置演示图形采用 **MIT** 许可证。兼容素材格式与交互体验参考了 [yugh6/Naiwa](https://github.com/yugh6/Naiwa)。原版图片、动作帧、笑声属于外部素材，由用户从作者项目获取，未包含在本仓库和发布包内。详细说明见 [NOTICE.md](NOTICE.md)。
