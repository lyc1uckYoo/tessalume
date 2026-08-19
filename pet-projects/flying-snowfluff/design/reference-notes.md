# 飞行雪绒：Codex 协议与项目参考

## OpenAI 官方行为

基准来源：[OpenAI Pets 文档](https://learn.chatgpt.com/docs/pets)

- 桌面宠物会表达 `Running`、`Needs input`、`Ready`、`Blocked` 等任务状态。
- `Running` 表示 Codex 正在处理任务；本项目因此用数据控制台表达，而不是让角色在地面奔跑。
- 开启减少动态效果时会显示静帧，所以每个状态的第一帧必须能独立表达语义。
- 自定义宠物保存在本机；安装后在 `Settings > Pets` 刷新、选择，再用 `/pet` 唤醒。
- 官方网页上传模板要求恰好 1536×1872、文件不超过 20 MiB。

本项目交付给桌面端当前本地图集播放器。1536×2288、8×11、192×208 单格和各行占用数来自本机现有桌面宠物资产与播放器行为的核对，不宣称是网页上传格式。两种格式不可混用。

## GitHub 工程参考

### [BeiXiao/awesome-codex-pets](https://github.com/BeiXiao/awesome-codex-pets)

- 宠物以独立 slug 目录安装到 `~/.codex/pets/<slug>/`。
- 展示页为各状态提供独立预览，便于在安装前发现状态混淆。
- 社区注册表面向 1536×1872 的网页／CLI 图集；本项目只借鉴包结构和预览方法，不直接套用其画布。

采用：安装目录最小化、按状态出预览、把展示产物留在构建目录而不是宠物目录。

### [Cute-chen/codex-pet](https://github.com/Cute-chen/codex-pet/blob/main/README.md)

- 可分发宠物目录保持精简，核心只放清单与图集；预览和投稿元数据放在外部工程目录。
- Windows 安装位置使用 `%USERPROFILE%\.codex\pets\<pet-id>\`。
- 项目展示逐项检查 Action、Idle、Waving、Running、Waiting、Review 等状态。

采用：正式安装目录只放 `pet.json` 与 `spritesheet.webp`，九种状态逐项验收。

### [Identity-safe avatar-to-pet guidance](https://gist.github.com/tumbling-dice/79b90e49de4703933b7f4c77510000a7)

- 生成提示应把身份边界限制在可见母版、用户明确提供的细节、项目笔记和格式约束内。
- 不应凭模型记忆自行补写角色部件。

采用：V6 母版、角色圣经和动作锚点三层锁定身份；动作帧不逐帧独立生成。

## 本项目的取舍

- 画风：采用平滑 2D 吉祥物插画，不继承旧像素风。
- 身份：只认 V6 母版；官方像素图只用于核对飞行雪绒的头环、服装、眼睛和翼链。
- 动作：先做透明姿势／视角锚点，再由脚本派生 74 个协议有效格，降低逐帧身份漂移。
- 特效：每个系统状态只保留一个稳定视觉符号——感叹号、控制台、纸飞机或故障断带；特效不能代替姿势。
- 发布：源码、锚点、规范和最终资产进入 Git；GIF、总览和校验报告从脚本重建。
