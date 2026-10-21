# 项目协作说明

- 本项目使用 C# 5 兼容语法与 Windows 自带的 .NET Framework 4.x 编译器，避免增加运行时依赖。
- 使用中文维护用户文档、菜单和错误提示；修改入口见 AI_GUIDE.md。
- 代码采用 MIT；原版素材属于外部输入，不提交原版源码、素材包、音频或视频。
- 修改前读取相关文件；保留身外跟随、身上回正、完整大笑、笑倒停留及全屏恢复行为。
- 构建：`./scripts/Build.ps1`。
- 测试：运行 `dist/NaiwaCompanion.exe --self-test test-output` 并检查进程退出码和 `results.txt`。
- 画面改动：运行 `--render` 检查生成预览；原版素材预览仅供本机检查，不提交到仓库。
- 打包前检查没有私人路径、凭证、日志和第三方素材；更新 release.json、SHA256SUMS.txt 和版本说明。
