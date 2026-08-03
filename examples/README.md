# Tessalume 旗舰主题模板 1.0

这是与仓库 Skill 同步生成的可运行示例。它固定了已验收旗舰主题的页面结构、
卡片尺寸、组件位置、聊天宽度和自适应显隐；开发新主题时只需要替换图片、
角色文案、颜色、纹样和主题动效。

## 在 Tessalume 中新建

打开软件的“使用说明”，点击“复制一份开始设计”，选择保存位置。软件会复制
这套完整模板；不要直接修改 `Templates/theme-template-v1` 中的内置原件。

## 在源码仓库中创建

```powershell
python .agents/skills/author-tessalume-theme/scripts/scaffold_theme.py `
  --repo-root . `
  --directory my-theme `
  --id creator.my-theme `
  --name "My Theme" `
  --author "github-user" `
  --namespace myt
```

脚手架生成后：

1. 替换 `assets/placeholder.svg`，并在 `manifest.json` 中为亮暗横幅、侧栏、
   聊天背景、记忆卡和三张角色卡分别声明资源。
2. 修改 `manifest.json` 的角色文案。
3. 在 `skin.css` 的固定章节中修改配色、图片裁切、角色纹样和动效；公共结构与几何由运行时共享模板提供。
4. 不要改动 `data-theme-role`、`data-theme-part`、主次优先级或
   `templateVersion: "1.0"`。
5. 不要编辑 CSS 最末尾的
5. ??? `skin.css` ????????????????????? `theme-template-v1.css` ???

## 校验

```powershell
python .agents/skills/author-tessalume-theme/scripts/sync_template_geometry.py `
  --check themes/my-theme

python .agents/skills/author-tessalume-theme/scripts/validate_theme_contract.py `
  --repo-root . themes/my-theme
```

主题只能使用包内已声明资源，不要加入远程图片、网络请求或本机绝对路径。

完整结构与尺寸表见
`.agents/skills/author-tessalume-theme/references/template-v1.md`。
