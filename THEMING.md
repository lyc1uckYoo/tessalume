# Tessalume 开放主题契约

Tessalume 负责 Codex 启动、本机 CDP、主题加载、资源解析、实时切换、生命周期、信任和故障恢复，不规定主题的视觉结构。

## v2 主题格式

- `type` 固定为 `advanced`。
- 必须提供 `theme.js`，可以另外提供 CSS；首次启用按包指纹确认信任。

Tessalume 不再提供普通 CSS 主题类型。导入成功后，主题直接保存到便携主题库的 `themes/<主题ID>`。

资源完全由主题清单命名，不存在固定的横幅、头像或拍立得要求。当前支持常见图片、字体、文本、JSON、音频和视频格式；单文件不超过 25 MiB，总资源不超过 100 MiB。

## 主题生命周期

`theme.js` 调用 `registerTheme({ mount, unmount? })`。切换主题时 Studio 先执行旧主题的 `unmount`，再逆序执行托管清理，移除 Studio 创建的样式、根节点、资源变量和主题类，最后挂载新主题。

主要上下文：

- `context.root`：本主题独占的 DOM 根节点。
- `context.config`：清单中的任意 JSON 配置。
- `context.assets` / `assetDataUrl(name)`：清单声明的本地资源。
- `context.mode`：当前 `light` 或 `dark`。
- `addCleanup(fn)`：注册切换时必须执行的清理。
- `on(...)`、`observe(...)`、`interval(...)`、`timeout(...)`：自动托管生命周期的辅助方法。
- `window` / `document`：当前 Codex 渲染页面，供完全自定义主题使用。

默认不在宠物浮层执行。主题只有明确声明 `compatibility.petOverlay: true` 才会在该页面挂载。
