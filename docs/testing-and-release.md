# 测试与发布说明

## 环境要求

- Windows 11
- Visual Studio 2022 或 Build Tools，需包含 MSBuild
- .NET Framework 4.8 targeting pack
- PowerShell 5+
- 可选：NSIS，用于生成传统安装器

## 构建

Debug 构建：

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' '.\Tomighty.sln' /t:Rebuild /p:Configuration=Debug
```

Release 构建：

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' '.\Tomighty.sln' /t:Rebuild /p:Configuration=Release
```

## 核心测试

```powershell
& '.\packages\NUnit.ConsoleRunner.3.4.1\tools\nunit3-console.exe' '.\Tomighty.Core.Test\bin\Debug\Tomighty.Core.Test.dll'
```

当前测试范围只覆盖 `Tomighty.Core` 的番茄钟行为。UI 改造必须保持这些测试通过。

## 一键完整验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File '.\test.ps1'
```

这会依次执行：

- Debug build
- NUnit core tests
- Timer window UI smoke
- Preferences/About/Error dialog UI smoke
- Release portable ZIP
- Release unsigned MSIX

如只想跑开发态验证、跳过发布产物：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File '.\test.ps1' -SkipRelease
```

## UI Smoke

```powershell
powershell -STA -NoProfile -ExecutionPolicy Bypass -File '.\tools\smoke-timer-window.ps1' -Configuration Debug
```

脚本会打开 `TimerWindow`，渲染一个 Pomodoro 状态，并输出截图：

```text
build\timer-window-smoke.png
```

这个 smoke 用于确认窗体可以实例化、绘制和关闭。它使用 `DrawToBitmap`，因此能稳定检查布局，但不能完整证明桌面 DWM 的 Mica/Acrylic 实时效果。

迭代行为断言时可跳过较慢的截图环节：

```powershell
$env:SMOKE_SKIP_CAPTURES = '1'
powershell -STA -NoProfile -ExecutionPolicy Bypass -File '.\tools\smoke-timer-window.ps1' -Configuration Debug
```

烟测宿主使用 `UnhandledExceptionMode.ThrowException`，消息循环里的异常会直接崩溃并输出堆栈，而不是卡在 WinForms 的异常对话框上。

## 发布

Portable ZIP：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File '.\publish.ps1' -Mode Zip -Configuration Release
```

传统安装器：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File '.\publish.ps1' -Mode Installer -Configuration Release
```

全部可用通道：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File '.\publish.ps1' -Mode All -Configuration Release
```

当前发布方式：

- Portable ZIP：已通过 `publish.ps1 -Mode Zip` 验证。
- NSIS installer：脚本已支持；需要本机安装 `makensis.exe` 后验证。
- Unsigned MSIX：已通过 `publish.ps1 -Mode Msix` 验证；产物可用于后续签名/安装流程。

MSIX artifact：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File '.\publish.ps1' -Mode Msix -Configuration Release
```

注意：当前 MSIX 是 unsigned。正式安装分发前需要证书签名，签名策略应单独决定。

## 当前验证结果

- Debug build：通过。
- NUnit core tests：12/12 通过。
- Timer window UI smoke：通过，截图输出到 `build\timer-window-smoke.png`。
- Dialog UI smoke：通过，截图输出到 `build\ui-smoke\preferences.png`、`about.png`、`error.png`。
- Release portable ZIP：通过，输出到 `dist\tomighty-windows-1.0.0.0.zip`。
- Release unsigned MSIX：通过，输出到 `dist\tomighty-windows-1.0.0.0.msix`。
