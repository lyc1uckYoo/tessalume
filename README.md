# Tessalume · 万棱流光

面向 Codex Desktop 的开源 Windows 主题工作室。Tessalume 用一个纯本地画廊管理、预览和切换完整沉浸式主题，不修改 Codex 安装包。

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/tessalume-dark.png">
  <source media="(prefers-color-scheme: light)" srcset="docs/screenshots/tessalume-light.png">
  <img alt="Tessalume 主题画廊" src="docs/screenshots/tessalume-light.png">
</picture>

## 为什么是 Tessalume

- **完整主题，而非换色皮肤**：主题可以同时提供角色视觉、聊天背景、组件样式、动画与交互逻辑。
- **真正的本地工作流**：主题、收藏、信任记录和偏好设置都保存在程序旁边，不提供账号、云同步、主题商店或远程下载。
- **安全边界清晰**：只连接 `127.0.0.1` / `::1` 上的 Codex 调试端口；主题按 SHA-256 指纹授权，源码变化后会重新确认。
- **可恢复、可诊断**：支持恢复 Codex 默认外观、本机端口诊断、主题包校验与单实例续接。
- **亮暗色一致**：主界面和原生标题栏均适配亮色、暗色，并记忆用户选择。

## 主题浮窗

浮窗提供收藏主题切换、默认外观恢复、Codex 明暗色切换，以及独立的 **5 小时额度** 与 **长周期额度** 圆环。若某个额度暂时未返回，会显示 `--`，恢复后自动出现。

![Tessalume 主题浮窗](docs/screenshots/tessalume-quick-switch.png)

## 使用

发布版可直接运行 `Tessalume.exe`。它是包含 .NET 运行时的自包含单文件程序，不需要另行安装运行环境。

首次启动时，程序会在 EXE 旁创建 `themes`、`Compatibility` 和 `data` 目录，并把内置主题释放到 `themes/<主题目录>`。以后完善的新主题直接放入该目录即可被本地画廊识别，也会自动参与源码构建，不需要维护硬编码名单。

当前仓库公开三套遵循同一注入契约的主题：

- `爱弥斯 · 星海远航`
- `心 · 朝月孤城`
- `达妮娅 · 泡影虚阈`

Tessalume 不会修改 WindowsApps、`app.asar` 或 Codex 用户数据。如果 Codex 需要以调试端口重启，程序会先取得用户确认。

## 构建

在 PowerShell 中运行根目录唯一的一键构建脚本：

```powershell
powershell -ExecutionPolicy Bypass -File ".\一键构建EXE.ps1"
```

脚本会根据 `global.json` 和项目文件自动完成：

1. 准备匹配的 .NET SDK；
2. 还原依赖并编译；
3. 运行全部自动检查；
4. 增量优化主题图片；
5. 发布自包含单文件 EXE。

默认产物为 `dist/portable-win-x64/Tessalume.exe`。构建只收集 `themes/` 的一级主题目录，以及各主题根清单明确声明的入口、预览和资源；`.sources`、`.references`、`.legacy` 与未声明文件不会进入 EXE。可使用 `-Configuration`、`-Runtime` 和 `-ThemeImageQuality` 覆盖默认参数；品牌名、SDK 版本、程序集名和输出文件名均从项目配置读取，不在脚本中重复写死。

## 制作主题

从 [旗舰主题模板 1.0](examples/README.md) 开始，然后在 Tessalume 画廊中导入。模板直接位于 `examples/` 根目录，不再保留旧的类型子目录；它以已验收的 `心 · 朝月孤城` 为结构基准，冻结首页横幅、左右卡片、状态面板、聊天内容和输入区的尺寸与位置。新主题只替换图片、角色文案、配色、纹样与专属动效，并直接位于 `themes/<主题目录>/`。

仓库内旗舰主题统一使用 canonical host：页面注入、路由状态、聊天消息、输出面板、侧栏标记与清理由运行时复用；主题包只替换资源、文案、颜色、角色元素和专属动效。完整包规范、生命周期与安全限制见 [THEMING.md](THEMING.md)，可执行模板与校验流程见 [.agents/skills/author-tessalume-theme](.agents/skills/author-tessalume-theme/SKILL.md)。

## 项目结构

```text
src/CodexThemeStudio.App       WPF 桌面界面、浮窗与本机运行适配
src/CodexThemeStudio.Core      主题加载、校验、启动与 CDP 核心
tests/                         无外部测试框架的自动检查
schemas/                       主题包规范
themes/<主题目录>/             公开主题源码；新增目录会自动参与构建
examples/                      可直接运行的旗舰主题模板 1.0
.agents/skills/                可复用主题创作规范、脚手架与契约校验
docs/screenshots/              仅包含 Tessalume 软件界面的公开截图
一键构建EXE.ps1               还原、检查、优化与发布入口
```

当前发布流程只生成可直接复制运行的单文件 EXE，不额外生成压缩包。
