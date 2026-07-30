# Tessalume · 万棱流光

Tessalume（万棱流光）是一个开源、纯本地的 Windows 视觉主题工作室，用来识别、导入和切换 Codex 本地主题，不修改 Codex 安装包。

## 使用

发布版可直接运行 `Tessalume.exe`；源码目录可通过根目录的 `一键构建EXE.ps1` 完整复现发布程序。这是包含 .NET 运行时的自包含单文件程序，不需要另外安装运行环境。

构建只生成自包含的 `Tessalume.exe`。首次启动时，EXE 会在旁边创建 `themes`、`Compatibility` 和 `data` 目录，并将内置沉浸式主题直接释放到 `themes/<主题目录>`。

- 不包含主题商店、账号、云同步、远程下载或联网更新。
- 只读取程序旁边 `themes` 中的本地主题库；每个主题直接对应一个独立子文件夹，不使用类型二级目录。
- 运行时仅连接 `127.0.0.1` / `::1` 上由 Codex 打开的本机调试端口。
- 如果 Codex 是普通方式启动，Tessalume 会先询问你是否允许重启；不会静默关闭 Codex。
- Tessalume 和主题都不会修改 WindowsApps、`app.asar` 或 Codex 用户数据。
- 当前仓库包含 `爱弥斯 · 星海远航` 与 `心 · 朝月孤城` 两个主题；以后完善的主题直接放入 `themes/<主题目录>` 即会作为公开主题参与提交和构建。

## 当前能力

- 只保留支持角色视觉、组件、动画与交互的沉浸式主题，不再提供普通 CSS 主题类型。
- WPF 本地主题库、图片预览和选择状态；Tessalume 界面支持亮色/暗色并记忆选择。
- 本地文件夹导入，导入前后各执行一次安全校验，并要求完整的 `theme.js` 生命周期。
- 本地主题库可随时刷新，并提供 Codex、回环端口和主题包诊断窗口。
- 自动发现或分配 Codex 本机调试端口。
- 主题持续注入、Tessalume 重启续接和恢复默认外观。
- 单实例运行；重复启动只会唤起已有窗口，不会产生多个注入看护器。
- 亮色/深色跟随 Codex；宠物浮层自动排除。
- 主题按 SHA-256 指纹授权；源码变化后必须重新确认。
- Tessalume 自身不访问公网；主题代码能力完整，启用前必须由用户确认信任。
- 构建时自动嵌入 `themes` 下的每个合法主题，无需维护硬编码名单；公开仓库未包含本地主题时也可正常构建。
- 内置与用户导入主题均可在 Tessalume 内删除；删除当前主题前会先恢复 Codex 默认外观。

## 构建

在 PowerShell 中运行唯一的一键构建脚本：

```powershell
powershell -ExecutionPolicy Bypass -File ".\一键构建EXE.ps1"
```

`global.json` 负责锁定 .NET SDK；脚本自动完成依赖还原、编译、自动检查、单文件发布和缓存清理。脚本优先复用 `DOTNET_ROOT`、系统 PATH 或本机已有的匹配 SDK，否则下载到与品牌无关的 `%LOCALAPPDATA%\dotnet-sdk-cache`；新电脑首次准备 SDK 和运行包需要联网。默认输出位于 `dist/portable-win-x64/{AssemblyName}.exe`，生成的 EXE 和软件运行过程不需要互联网。

构建脚本会从 `global.json` 读取 SDK 版本、从应用项目读取程序集与 EXE 名称，不重复写死品牌信息。可通过 `-Configuration`、`-Runtime` 和 `-ThemeImageQuality` 覆盖默认构建参数。

## 制作新主题

从 [沉浸式主题模板](examples/advanced-theme/README.md) 开始，然后在 Tessalume 的主题画廊中导入。资源数量和页面结构均由主题自行决定；完整契约见 [THEMING.md](THEMING.md)。

## 目录结构

```text
src/CodexThemeStudio.App       WPF 桌面界面与受信任布局适配器
src/CodexThemeStudio.Core      主题、启动、本机 CDP 与诊断核心
tests/                         零外部依赖自动检查
schemas/                       主题包规范
themes/<主题目录>/             构建时自动嵌入的沉浸式主题源码（默认不提交）
examples/advanced-theme/       沉浸式主题开发模板
一键构建EXE.ps1               还原、测试、发布与清理脚本
```

当前不生成压缩包，发布产物保持为一个可直接复制的自包含 EXE。
