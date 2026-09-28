# Tessalume 测试工程

`Tessalume.Tests` 是面向 Windows 产品工程、不依赖第三方测试框架的回归套件，由项目的一键构建流程直接运行。测试项目正式引用 `Tessalume.Core` 与 `Tessalume.App`，不重复链接编译产品源码。

## 结构

- `Program.cs`：唯一入口，仅启动测试套件。
- `TestSuite.Runner.cs`：运行时探针参数分发、测试清单与结果汇总。
- `Tests/ThemePackageTests.cs`：主题包加载、校验、导入和修订指纹。
- `Tests/RuntimeTests.cs`：主题运行时、资源分段、恢复和页面修饰。
- `Tests/BackupTests.cs`：用户数据备份、内容摘要、哈希校验、内置主题保护、取消、事务恢复和回滚。
- `Tests/CompatibilityTests.cs`：Codex/运行时兼容性基线、失败阶段持久化和诊断页接线。
- `Tests/ReleaseCandidateTests.cs`：覆盖 1.2 配置迁移、项目发现、体检、分享 ZIP、干净导入、用户数据备份与事务恢复的隔离端到端流程。
- `Tests/TemplateContractTests.cs`：Template 1.0、冻结几何和内置主题契约。
- `Tests/AppLifecycleTests.cs`：配置迁移、启动、恢复和自动更新接线。
- `Tests/ProductSurfaceTests.cs`：主界面、设置、诊断、无障碍、个人图片与视觉调节边界。
- `Tests/ThemeLibraryExperienceTests.cs`：主题最近使用排序、版本比较、拖放来源判断、详情交互和配置持久化。
- `Tests/CreatorWorkflowTests.cs`：Codex 角色提示词、草稿持久化、创作者工作区版本契约、安全升级与主题创作交接。
- `Tests/CreatorProjectTests.cs`：最近工作区、结构化创作体检、稳定文件监听、健康门控自动应用和确定性主题导出。
- `Tests/ReleaseEngineeringTests.cs`：源码边界、构建入口和发布资产约定。
- `Tests/UpdateTests.cs`：Release 检查、校验、替换、回滚和用户数据保留。
- `RuntimeProbeCommands.cs`：面向实际 Codex 会话的命令行运行时探针。
- `CreatorSnapshotCommands.cs`：创作项目中心亮色、提示词编辑器、滚动详情和暗色界面截图验收。
- `StageDSnapshotCommands.cs`：关于与数据页、兼容性诊断页亮色/暗色截图及滚动验收。
- `ArtworkSnapshotCommands.cs`：高级图像编辑器基础、构图、效果及亮暗模式截图验收。
- `ThemeLibrarySnapshotCommands.cs`：主题画廊、亮色详情与暗色详情的大图预览截图验收。
- `ArtworkLibrarySnapshotCommands.cs`：角色列表、相册与图片详情的亮暗截图，以及紧凑相册、左栏、右侧主卡和记忆卡详情，使用隔离数据与当前源码中的原图。
- `Tests/ArtworkLibraryStorageTests.cs`、`Tests/ArtworkLibraryExperienceTests.cs`：图库迁移、独立图片分类、收藏、分级导航、原图比例、构图隔离、备份兼容、异步预览失效、保存失败回滚与精确应用回归。
- `Tests/ArtworkLibrarySlotTests.cs`：所有已发布角色的完整原图索引，七个图片位置和亮暗十四个目标，新增左卡／记忆／右侧双卡的精准应用、恢复、撤销、持久化与保存失败回滚，以及传给运行时的独立图片指纹和效果。
- `Tests/ArtworkLibraryDeletionTests.cs`：主题原图与已准备原图的身份保护、个人导入删除后的持久隐藏与重导入、元数据清理、失败写入回滚、只读防护、备份还原，以及确认取消、返回相册、已应用图片和撤销记录保留。
- `TestInfrastructure.cs`：仓库定位、主题夹具和共用断言。

新增回归检查时，应放入对应功能文件并在 `TestSuite.Runner.cs` 注册。只有多个测试类别共用的代码才进入 `TestInfrastructure.cs`。

## 运行

正常开发与发布统一使用仓库根目录的完整构建：

```powershell
powershell -ExecutionPolicy Bypass -File ".\一键构建EXE.ps1" -NoLaunch
```

已有匹配 SDK 和还原结果时，也可以只运行测试项目：

```powershell
dotnet run --project .\tests\Tessalume.Tests\Tessalume.Tests.csproj -c Release
```

图库开发时可先执行 `--artwork-library-tests`；最终仍运行完整一键构建。运行前将当前进程的 `TEMP` 和 `TMP` 指向 `E:\Tessalume-QA\artwork-library-temp`，图库体验夹具也在该目录创建独立子目录，避免工作区扫描程序占用临时文件。

```powershell
dotnet run --project .\tests\Tessalume.Tests\Tessalume.Tests.csproj -c Release -- --artwork-library-tests
```

下面的 `--artwork-library-…` 截图选项同样放在 `dotnet run` 的 `--` 之后，或直接作为已编译测试程序的参数。

`--artwork-library-album-snapshots <outputDir>` 使用当前编译的 WPF 页面，一次生成六张角色相册流程截图：`characters-light.png`、`characters-dark.png`、`album-light.png`、`album-dark.png`、`album-compact.png` 和 `detail-sidebar-compact.png`。角色列表和单角色相册亮暗图使用正常窗口 1280×820，紧凑相册和左栏详情为 1080×820。截图会验证普通入口首先显示角色列表，进入相册后只显示该角色的图片，再打开图片详情。

`--pet-gallery-snapshots <gallery-light.png> <gallery-dark.png> <detail-light.png> <detail-dark.png> [pet-id]` 生成全部已发布宠物的画廊和指定宠物的详情亮暗截图；不传 `pet-id` 时保持使用 `phoebe-jiubi`，清宵使用 `qingxiao`。运行时将 `TEMP` / `TMP` 设到 E 盘隔离目录，截图不会修改真实 Codex Pets 安装。

`--artwork-library-card-snapshots <outputDir>` 单独生成两张 1280×820 详情图：右侧主卡亮色 `card-detail-light.png` 与记忆卡暗色 `memory-detail-dark.png`。截图前验证实际选中位置、亮暗模式、原图路径和主题默认状态，不重复生成相册流程截图。

`--artwork-library-delete-snapshots <outputDir>` 在隔离图库导入当前主题原图的个人副本，生成两张 1280×820 删除入口详情图：`import-delete-light.png` 和 `import-delete-dark.png`。同时验证主题原图不显示删除入口、个人副本显示可用入口；不会删除或修改用户图片。输出建议使用 `E:\Tessalume\artifacts\qa\library-delete`。

输出建议使用 `E:\Tessalume\artifacts\qa\artwork-library`。截图夹具数据在该目录的独立隐藏子目录中，遵守便携布局的数据边界；截图使用当前源码主题及隔离配置，不连接或应用到用户正在使用的 Codex。

兼容命令 `--artwork-library-flow-snapshots <outputDir>` 继续生成 `gallery-light.png`、`gallery-dark.png`、`detail-hero-light.png`、`detail-chat-dark.png` 和 `detail-sidebar-compact.png`。其中 gallery 为单角色相册，两张图使用 1280×1620，便于检查完整相册；首页和聊天详情为 1280×820，紧凑左栏详情为 1080×820。

旧命令 `--artwork-library-snapshots <light.png> <dark.png> <compact.png>` 继续输出首页亮色、聊天暗色和紧凑左栏三个图片详情截图，供已有脚本兼容。

`python tools/test-runtime-surfaces.py --output artifacts/qa/split-view` 在独立的真实 Chromium 无头进程中验证 30 个页面／分屏场景，包括文件独占、聊天与文件分屏、原生标签栏和遗留输入框的隔离。它使用当前源码运行时、独立浏览器数据目录和调试端口，不连接或操作用户正在使用的页面；结果写入输出目录的 `runtime-surfaces.json`。需要本机 Chrome 或 Edge 及 Python `websocket-client`，可用 `--browser <exe>` 指定浏览器。
