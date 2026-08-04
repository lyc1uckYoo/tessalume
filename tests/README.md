# Tessalume 测试工程

`Tessalume.Tests` 是面向 Windows 产品工程、不依赖第三方测试框架的回归套件，由项目的一键构建流程直接运行。测试项目正式引用 `Tessalume.Core` 与 `Tessalume.App`，不重复链接编译产品源码。

## 结构

- `Program.cs`：唯一入口，仅启动测试套件。
- `TestSuite.Runner.cs`：运行时探针参数分发、测试清单与结果汇总。
- `Tests/ThemePackageTests.cs`：主题包加载、校验、导入和修订指纹。
- `Tests/RuntimeTests.cs`：主题运行时、资源分段、恢复和页面修饰。
- `Tests/TemplateContractTests.cs`：Template 1.0、冻结几何和内置主题契约。
- `Tests/AppLifecycleTests.cs`：配置迁移、启动、恢复和自动更新接线。
- `Tests/ProductSurfaceTests.cs`：主界面、设置、诊断、无障碍和视觉调节。
- `Tests/CreatorWorkflowTests.cs`：Codex 创作者工作区与主题创作交接。
- `Tests/ReleaseEngineeringTests.cs`：源码边界、构建入口和发布资产约定。
- `Tests/UpdateTests.cs`：Release 检查、校验、替换、回滚和用户数据保留。
- `RuntimeProbeCommands.cs`：面向实际 Codex 会话的命令行运行时探针。
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
