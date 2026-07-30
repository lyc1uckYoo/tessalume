# 沉浸式主题模板

主题拥有自己的 `theme.js`，第一次启用以及文件内容变化后需要用户按 SHA-256 指纹重新确认信任。

脚本必须调用：

```javascript
registerTheme({
  async mount(context) {},
  async unmount(context) {}
});
```

`context` 提供主题根节点、任意资源、清单配置、当前亮暗模式，以及自动清理的事件、观察器、定时器和回调注册能力。主题也可直接使用当前页面的 `window` 与 `document`。

主题能够读取 Codex 页面内容，也可能自行联网；请只启用你信任的源码。

## 制作卡片预览图

预览图会显示在 Tessalume 主题卡片的上半部分。你可以使用实际界面截图展示组件和布局，也可以直接使用角色图、风景图或品牌主视觉等主题横幅。

1. 推荐尺寸为 `1600 × 600`（约 8:3），支持 PNG、JPG、WebP 等常见位图格式，单张需小于 25 MB。
   卡片会居中裁切图片，请把人物面部、标志和主要视觉放在画面中部安全区域。
2. 将图片放在当前主题的 `assets` 文件夹，例如 `assets/preview-light.png` 和 `assets/preview-dark.png`。
3. 在 `manifest.json` 中声明：

```json
"previews": {
  "light": "assets/preview-light.png",
  "dark": "assets/preview-dark.png"
}
```

亮暗模式可以使用不同图片；如果共用一张，让两项指向同一路径。若主题已经有 `assets/hero.jpg` 等横幅资源，可以直接把 `light` 和 `dark` 都指向它，不必额外复制一张预览图。预览路径相对于主题文件夹，不要求同时出现在 `assets` 资源表中。

本模板当前直接使用 `assets/emblem.jpg` 横幅作为示例预览，演示的就是“复用主题横幅”方案。你也可以换成 `preview-light.png` / `preview-dark.png` 两张实际界面截图。
