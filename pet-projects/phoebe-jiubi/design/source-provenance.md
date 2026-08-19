# 素材来源与使用边界

## 用户参考

- `references/user/phoebe-jiubi-style-reference.png`
- SHA-256：`8608314C4F733E5C0724AE94938EB4F007F4E9A251FEA880ED77DC1032366FAE`
- 用途：用户明确认可的头身比例、软萌气质与身份符号参考；不作为最终图集像素来源。

- `references/user/phoebe-jiubi-turnaround-reference.png`
- SHA-256：`D4B66722AB2329C790F36B4D470A9B6C734628D61ED5CB61ABB93DB35EE88849`
- 用途：用户补充的正、侧、背结构参考，用于锁定发夹侧别、帽带环绕、长发背面轮廓与服装关系；不直接切图或并入最终图集。

- `references/user/jump-motion-reference.png`
- SHA-256：`AC881FE6C08304D19D48417600929C8D24D627F415E0BD768AD0331D0446F236`
- 用途：用户指定的跳跃节奏与肢体展开参考；只提炼“蓄力—升空—顶点—下降—落地”以及欢迎式张手的动作语义，不复制参考角色或像素画面。

- `references/user/idle-seated-reference.jpg`
- SHA-256：`0A4BAFDB3BFA11335ED36FE01EB8A85E5F37B4C72A25EB90C0D5AF42164885D9`
- 用途：用户指定的坐姿待机参考；只提炼正面松弛坐姿与“仅眨眼”的动作语义，不复制其中社区线稿、帽子变体或像素。

- `references/user/idle-exact-source.png`
- SHA-256：`615B481BCDD7FAFC3F18B4FD751796B478E1F0B06823BA9C02876A37AE704D5F`
- 用途：用户在 2026-08-19 明确指定为待机动作的逐像素母版，并要求“和这个图一模一样，只补眨眼”。待机睁眼帧直接保留该图主体；半闭眼与闭眼帧只允许更换双眼内部/眼皮区域，其他区域由确定性合成保持不变。右侧边缘的邻图残片不属于角色主体，清理后不进入图集。

## 社区桌宠技术对照

- `https://github.com/sherlidian01-web/phoebe-codex-pet`：流行 Codex 桌宠示例；仓库未声明许可证，只观察 v2 图集结构，不复制任何文件。
- `https://github.com/alan890104/phoebe-chubby-codex-pet`：MIT；观察完整状态覆盖、打包和预览组织，不复制角色图像。
- `https://github.com/KanadeK/feibi-jiubi-codex-pet`：MIT；观察“姿势表到图集”的可复现制作思路，不复制姿势表、提示词或图像。
- `https://github.com/Squirtleeeee/Fbjbdesktoppet` 与 `https://github.com/YinLinF7/FeiBiJiuBiDesktopPet`：仓库代码采用 MIT，但各自角色美术另有来源或权利声明；只观察状态机与程序化帧处理，不复制其角色图像。
- 本项目的视觉生成、关键帧和确定性编排均重新制作；若后续确实移植上游代码，必须先保存其 LICENSE/NOTICE 并在这里记录具体文件与修改。

## 角色权利

这是非官方同人宠物。菲比、《鸣潮》及相关名称、角色设计与商标权利归其各自权利人所有；本项目不会把社区作者的公开发布误称为可自由复用素材。
