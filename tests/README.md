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
