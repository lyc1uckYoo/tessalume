# Tessalume 旗舰主题模板 1.0

这是与仓库 Skill 同步生成的创作工具链 2.0 空白模板。它固定了已验收旗舰
主题的页面结构、卡片尺寸、组件位置、聊天宽度和自适应显隐，并把六张可调
原图与主题推荐构图彻底分离。

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
3. 在 `artwork-defaults.json` 中填写首页横幅、左栏图片、聊天背景的亮暗六槽
   推荐构图、效果、遮罩和可选相对动效；每槽继续引用 manifest 中同名原图。
4. 在 `skin.css` 的固定章节中修改配色、文字、角色纹样、卡片和独立装饰动效。
   不得在 CSS 中引用六张可调图片或写入其裁切、滤镜、透明度、遮罩与图片动画。
5. 不要改动 `data-theme-role`、`data-theme-part`、主次优先级或
   `templateVersion: "1.0"`。
6. 不要把共享几何复制回 `skin.css`；尺寸与位置由运行时的
   `theme-template-v1.css` 统一提供。
7. 修改准备发布的六槽推荐值时递增 `defaultsVersion`；不要读取或写入用户的
   个性化覆盖。

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
