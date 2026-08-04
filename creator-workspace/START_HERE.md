# 用 Codex 一句话制作 Tessalume 主题

这个文件夹已经包含 Tessalume Template 1.0、主题创作 Skill、11 张标准素材规范、
共享几何和自动校验脚本。你不需要手动修改模板，也不需要先学 CSS。
工作区根目录的 `TESSALUME_CREATOR_WORKSPACE.json` 用于识别工具链版本；以后通过
Tessalume 安全升级时只更新 Skill、Schema、说明和共享校验文件，不会改动 `themes/` 中的项目。

## 开始

1. 在 Codex 中打开整个 `Tessalume-Creator` 文件夹。
2. 新建任务并发送一句话，例如：

   > 请为《鸣潮》的椿制作一套 Tessalume 主题。

   也可以附上参考图片，并说明希望的色调、服装形态或氛围。
3. Codex 会先研究角色，并给出完整的 11 张素材计划。确认方案后，它才会开始
   生成素材、编写主题并运行校验。
4. 完成后，Codex 会告诉你最终主题位于 `themes/<主题目录>`。
5. 回到 Tessalume，点击“导入主题”，选择这个主题目录即可；分享给别人时，也可以把这个目录压缩为 ZIP 后直接导入。

## 一句话可以说到什么程度

- 最简单：`请为《作品名》的角色名制作一套 Tessalume 主题。`
- 带偏好：`请为角色名制作一套蓝白月光风 Tessalume 主题，参考我附的图片。`
- 指定形态：`请以角色名的某个形态为主，制作亮暗完整的 Tessalume 主题。`

一句话负责启动完整流程。为避免生成错误角色或浪费 11 张图片，Codex 会在正式
生成前让你确认一次角色身份卡和素材计划。

## 文件夹说明

```text
.agents/   Codex 自动发现的 Tessalume 主题创作 Skill
schemas/   主题清单格式
src/       Template 1.0 共享几何，只用于校验，不要修改
themes/    Codex 在这里创建主题；完成后从这里导入
```
