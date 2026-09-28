# 清宵宠物开发项目

本项目保留用户认可的母图形象，并逐动作制作清宵宠物。主制作记录位于 `build/hatch-run-v2`。

## 正式包

清宵 `1.0.0` 已按本次发布授权晋升至仓库 `pets/qingxiao`，随 Tessalume `2.2.0` 集成发布。正式包只包含 `pet.json`、无损透明图集、11 个 GIF 和严格哈希 catalog；配套主题为 `qingxiao.cloudsword-gate`。原图和预览画面沿用已通过验收的候选，不作重新生成或改绘。

`build/` 中的制作记录和候选保持本地保存，不属于 Git 或应用发布资源。全新检出无需这些记录即可通过 `pets/qingxiao` 验证、构建、预览和安装正式包。下文候选路径只服务于继续制作与 QA，不能作为发布管线的输入。

## 当前素材

- 母图：`build/hatch-run-v2/references/reference-01.png`
- 角色基准：`build/hatch-run-v2/references/canonical-base.png`
- 动作源图：`build/hatch-run-v2/decoded/`
- 动作提示词：`build/hatch-run-v2/prompts/`
- 本次提示补充与进度：`build/hatch-run-v2/qa/resume-2026-09-28.md`
- 独立标准动作检查：`build/hatch-run-v2/qa/standard-visual-qa.json`
- 单动作联系表和预览：`build/hatch-run-v2/qa/resume-2026-09-28/`
- 已验收候选备份：`build/gallery-candidate/`
- 最终图集与透明边缘校验：`build/hatch-run-v2/final/validation-extended.json`
- 独立逐帧与十六向复核：`build/hatch-run-v2/qa/final-visual-qa.json`
- 左右御剑并排预览：`build/hatch-run-v2/qa/previews/sword-flight.gif`

## 形象约束

以母图和已认可待机为准，保持大头、紧凑躯干与完整腿部的成熟 Q 版比例。必须保留流云双角、蓝色长发、白蓝衣装、玉饰、透明长袖、单柄云琅；不使用 OpenAI 标记，不加轮廓发光、场景或速度线。动作不能改变身体比例、增生肢体或让刚性剑伸缩。

## 制作与预览

视觉素材通过内置 imagegen 生成，每行单独制作；提取、尺寸规范、透明背景处理、图集组装和检查由 hatch-pet 工具处理。

`tools/review_row.py` 只从已提取帧制作联系表和 GIF。`tools/package_candidate.py` 只包装通过验证的最终图集、真实动作 GIF、十六向 GIF 与九宫格，不生成动作或填补空行。

后续修改应从对应动作源图开始，重新验收整行，再更新候选包；保留已通过的其他行。开发候选不自动晋升为仓库 `pets/qingxiao` 正式发布包。

本次完成九种标准动作、十六向注视和动态九宫格，共 11 个真实 GIF 预览。左右御剑各 8 帧，脚下保留完整横向云琅，以轻微悬浮、发袖尾随和附着剑尾的短流风表现移动；不添加人物轮廓发光。动作源图、整行重试、提示词和 QA 均保留，以便按用户反馈逐动作修改。原待机候选另保存在 `build/gallery-candidate-idle-backup-20260928/`。

本次用户已明确授权清宵与图库功能一起发布新版本。历史预览记录与正式晋升校验见 `build/hatch-run-v2/qa/run-summary.json`；应用构建和发布由主任务统一完成。
