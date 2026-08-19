# 飞行雪绒 Codex 桌宠 V1

这是《鸣潮》爱弥斯电子幽灵形态“飞行雪绒”的独立 Codex 桌宠项目。V1 采用平滑 2D 游戏吉祥物画风，不沿用早期像素画风；正常眼睛是默认形态，墨镜只在完成动作的一帧彩蛋中出现。

项目版本由 `VERSION` 记录为 `1.0.0`。`pet.json` 中的 `spriteVersionNumber: 2` 是 Codex 桌面图集协议号，两者不是同一版本。

当前正式图集 SHA-256：

```text
84F00F129959636BA2F577A070AFE7C02E5BDCAA79775D7802EB3DC8D2781BAB
```

## 真正参与最终构成的文件

| 类别 | 路径 | 用途 |
| --- | --- | --- |
| 身份母版 | `assets/identity/flying-snowfluff-master.png` | 唯一不可漂移的人物设计母版；构建时校验固定哈希 |
| 中立帧 | `assets/identity/reduced-motion-neutral.png` | “减少动态效果”使用的独立待机帧 |
| 动作源 | `assets/keyframes/` | 56 张透明关键姿势与 2 张工作态翼链修复层 |
| 构建逻辑 | `tools/build_smooth_pet.py` | 编排动作、微循环、特效、对齐、预览和 8×11 图集 |
| 协议清单 | `pet.json` | Codex 桌宠名称、状态帧数与协议版本 |
| 正式安装资产 | `spritesheet.webp`、`pet.json` | 可以复制到 Codex 本地宠物目录的最终文件 |

`references/` 与 `design/` 不会直接画进最终图集：前者用于核对官方身份、像素原型和画风方向，后者记录人物约束、动作设计与最终验收结论。`build/` 只包含可重建的候选、预览和报告，已被 Git 忽略，随时可以清空。

## 目录结构

```text
flying-snowfluff/
├── AGENTS.md                   # 本宠物不可违反的制作规则
├── README.md                   # 项目入口
├── VERSION                     # 产品版本；当前为 1.0.0
├── pet.json                    # 正式协议清单
├── spritesheet.webp            # 正式安装图集
├── assets/
│   ├── identity/               # 唯一母版与减少动态效果中立帧
│   └── keyframes/              # 56 张实际动作源与 2 张局部修复层
├── references/
│   ├── canonical/              # 早期官方像素图集，只作核对
│   ├── official/               # 当前保留的官方墨镜形态参考
│   ├── studies/                # 从正式素材整理的身份与姿势研究板
│   └── style/                  # 用户认可的非像素画风方向
├── design/                     # 角色圣经、动作规范和验收记录
├── tools/                      # 构建、协议校验、动作审计工具
└── build/                      # 可重建输出，不提交 Git
```

参考资料的来源与使用边界见 `references/README.md`。设计文档中，`character-bible.md` 管人物身份，`animation-spec.md` 管状态和协议，`motion-polish-notes.md` 管动作制作方法，`final-motion-review.md` 记录 V1 最终数据，`reference-notes.md` 记录 Codex 协议与外部项目研究。

## 已完成动作

- 待机：六个完整姿势组成悬浮呼吸、侧看、眨眼、翼链与编发微循环。
- 左右移动：连续侧向飞行，翼链、辫发、手臂与腿部按相位运动。
- 挥手／触屏：招呼、前探、触碰与回位闭环。
- 跳跃：压缩蓄力、蹬离、明确升空、制动、落地回弹。
- 阻塞：受惊、信号衰减、蜷缩故障、重新连接与恢复，并以持续故障徽记保持语义。
- 等待输入：按听见、倾听、指示、邀请、等待与提醒的自然顺序表达请求。
- 正在工作：双手操作悬浮控制台，左右翼链完整参与扫描与交叉核验。
- 完成待看：从收到通知到掷出纸飞机、庆祝并展示完成状态；仅一帧墨镜。
- 环绕视线：正面、侧面、四分之三面和背面的 16 向连续转身。

## 构建与验证

在本目录运行：

```powershell
python tools/build_smooth_pet.py
python tools/pet_pipeline.py validate --sheet build/final-motion-candidate/spritesheet.webp --report build/final-motion-candidate/validation-report.json
python tools/audit_motion_quality.py --original references/canonical/canonical-spritesheet.webp --baseline spritesheet.webp --candidate build/final-motion-candidate/spritesheet.webp --original-label original-pixel --baseline-label installed-current --candidate-label final-candidate --out build/motion-audit-final
python -m py_compile tools/build_smooth_pet.py tools/pet_pipeline.py tools/split_keypose_sheet.py tools/audit_motion_quality.py
```

候选位于 `build/final-motion-candidate/`，包含图集、清单、状态 GIF、16 向转身 GIF、跨背景检查板、完整图集总览和量化报告。三版本逐帧审计位于 `build/motion-audit-final/`。

## Codex 图集协议

桌面端本地图集固定为 1536×2288、8 列×11 行、每格 192×208、透明无损 WebP，`spriteVersionNumber` 为 `2`。这里的 `2` 是桌面图集协议号，不是产品版本号。

普通状态占用格依次为 `7, 8, 8, 4, 5, 8, 6, 6, 6`，最后两行的 16 个方向格全部占用，共 74 个有效格；未使用格必须完全透明。

## 本地安装

安装目录只保留两个文件：

```text
%USERPROFILE%\.codex\pets\aemeath\
├── pet.json
└── spritesheet.webp
```

复制后，在 Codex 的 `Settings > Pets` 中刷新并选择“飞行雪绒”，再用 `/pet` 唤醒。网页上传模板是 1536×1872，与本项目桌面本地 8×11 图集不同，不要混用。
