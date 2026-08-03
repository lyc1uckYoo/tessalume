# Tessalume 安全与隐私

## 支持范围

公开发布后，只维护最新的 `1.2.x` 版本。报告问题前请先确认使用的是 Releases 页面中的最新构建。

## 本机边界

- Tessalume 只连接本机回环地址，不提供账号、云同步、主题商店或远程下载。
- 软件不修改 Codex 安装包、`app.asar` 或 Codex 用户数据。
- 主题包中的 CSS、JavaScript 与本地资源用于构建 Codex 的视觉主题；导入器会检查包结构、路径、文件类型与大小，并拒绝远程 CSS 资源、越界路径和 ZIP 链接条目。
- 诊断日志只保存在 `data/logs/`，不会自动上传。复制诊断报告时，常见 Windows 用户目录会替换为 `%USERPROFILE%`、`%LOCALAPPDATA%` 和 `%TEMP%`。

## 报告安全问题

请优先使用仓库的 [Security Advisory 私密报告入口](https://github.com/lyc1uckYoo/tessalume/security/advisories/new)，不要在公开 Issue 中粘贴敏感信息、账号信息或未检查的完整日志。

普通缺陷请使用 [Bug 报告模板](https://github.com/lyc1uckYoo/tessalume/issues/new?template=bug-report.yml)，并附上复现步骤和 Tessalume 诊断报告。
