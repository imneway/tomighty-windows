# Tomighty Windows 现代 GUI 改造计划

## 仓库现状

- 源仓库：https://github.com/imneway/tomighty-windows
- 本地路径：`D:\Dropbox\Personal\Dev\tomight-windows`
- 当前分支：`master`
- 当前技术栈：.NET Framework 4.5/4.5.2 + WinForms
- 主解决方案：`Tomighty.sln`
- 项目结构：
  - `Tomighty.Core`：番茄钟核心逻辑、事件、计时器、偏好设置接口
  - `Tomighty.Core.Test`：基于 NUnit 的核心行为测试
  - `Tomighty.Windows`：托盘应用、WinForms 计时窗口、偏好设置、通知
  - `Tomighty.Update.Swap`：旧版自动更新辅助程序

## 当前架构判断

- 这是一个托盘优先的应用。`TomightyApplication` 负责组装事件总线、计时器、用户偏好、番茄钟引擎、托盘菜单、通知、声音和自动更新。
- 核心逻辑和 UI 分离得还不错，`Tomighty.Core` 可以保留下来，作为现代 GUI 的业务内核。
- 当前 UI 是传统 WinForms：自绘无边框计时小窗、`ContextMenuStrip` 托盘菜单、模态偏好设置窗口、关于窗口和错误窗口。
- 用户偏好通过 JSON 写入 AppData。
- 构建和发布方式偏旧：`packages.config`、NSIS、`dist.bat`、旧版 `GitVersionTask`。
- 当前本机有 .NET SDK 9，但 PATH 里暂时没有 `msbuild.exe` 和 `nuget.exe`。后续需要先让项目能用现代命令行稳定构建。

## 视觉目标：Mica + Acrylic

目标不是简单“换皮”，而是做成 Windows 11 风格的现代桌面体验。

- 主计时窗口使用 Mica 作为窗口底层材质。
- 偏好设置窗口也可以使用 Mica，保持稳定、低干扰、系统一致的背景。
- 托盘弹出菜单、轻量弹层、临时面板使用 Acrylic。
- 对话框遮罩或阻塞式确认可以使用 Smoke 或普通半透明遮罩。
- 控件层使用 Fluent 风格：清晰层级、圆角适度、系统主题、可访问焦点态、高 DPI 友好。

参考：

- Microsoft Learn: Materials overview  
  https://learn.microsoft.com/en-us/windows/apps/develop/ui/materials
- Microsoft Learn: System backdrops, Mica/Acrylic  
  https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/system-backdrop-controller

## Mica 和 Acrylic 的区别

Mica 是不透明材质，会吸收用户主题和桌面壁纸的色彩，形成轻微个性化的窗口背景。它性能更好，因为它不是持续实时模糊背后的窗口，而更像是取一次桌面背景信息后生成底层视觉。因此它适合长期存在的窗口底板，例如主窗口、标题栏、设置页。

Acrylic 是半透明毛玻璃材质，会制造“能隐约看到背后内容”的深度感。它更适合短暂出现、可轻 dismiss 的 UI，例如 flyout、context menu、popup、临时侧栏。Acrylic 视觉更明显，但过度使用会显得花，也更容易影响可读性。

简化成设计规则：

- Mica：主窗口背景，安静、稳定、性能友好。
- Acrylic：弹出层和菜单，轻盈、有玻璃感、强调临时层级。
- 不建议整套 UI 到处 Acrylic；番茄钟这种工具型应用应该让计时信息优先。

## 推荐技术路线

推荐采用分阶段现代化，而不是一次性重写：

1. 先保留并现代化 `Tomighty.Core`。
2. 把解决方案迁移到 SDK-style 项目格式和当前 .NET 目标框架。
3. 新建现代 Windows GUI 外壳，复用核心逻辑。
4. 最后替换旧版更新、安装和发布流程。

GUI 首选路线是 **WinUI 3 / Windows App SDK**。它天然支持 Windows 11 的 Mica、Acrylic、Fluent 控件、现代通知和更自然的打包方式。备选路线是现代 .NET WinForms 加自定义 DWM/Mica 处理，但那更像“刷新旧应用”，不如 WinUI 3 正统。

当前实施路线：

- 先将现有工程 retarget 到 .NET Framework 4.8，恢复可构建和可测试状态。
- 在现有 WinForms UI 上接入 Windows 11 DWM 的 Mica/Acrylic backdrop，保证核心逻辑不动。
- 后续如果要进一步接近原生 Fluent 控件体验，再评估 WinUI 3 app shell。

## 阶段 0：建立安全基线

- 补充开发环境说明：SDK、构建、测试、运行方式。
- 处理 Git `safe.directory` 问题，保证本地命令可用。
- 恢复旧 NuGet 包，或直接迁移掉 `packages.config`。
- 跑通现有核心测试，记录当前番茄钟行为。
- 在能命令行构建后添加轻量 CI。

交付物：

- 一次干净的基线提交，包含开发说明和可运行的核心测试。

## 阶段 1：现代化项目系统

- 将 `Tomighty.Core` 迁移为 SDK-style C# 项目。
- 根据 GUI 路线选择目标框架：优先 `net9.0`，如需兼容可考虑 `netstandard2.0`。
- 将 `Tomighty.Core.Test` 迁移为 SDK-style，并升级 NUnit/NSubstitute。
- 移除旧版 `GitVersionTask`，换成现代版本号策略。
- 决定 `Tomighty.Windows` 是暂时保留为兼容宿主，还是直接被新 app 替换。

交付物：

- `dotnet build` 和 `dotnet test` 能跑通核心项目。

## 阶段 2：新 GUI 外壳

推荐方案：

- 新建 `Tomighty.App`，使用 WinUI 3 / Windows App SDK。
- `Tomighty.Core` 继续保持 UI 无关。
- 增加 ViewModel 层，承接计时状态、命令、偏好设置和托盘动作。
- 抽象通知、声音、存储、启动项、更新等 Windows 集成能力。
- 主窗口使用 `MicaBackdrop`，临时弹层使用 `DesktopAcrylicBackdrop` 或 Acrylic brush。

备选方案：

- 将 `Tomighty.Windows` 迁移到现代 .NET WinForms。
- 重做计时窗口和偏好设置界面。
- 尽量保留当前托盘和通知逻辑。

决策建议：

- 想要真正的 Windows 11 质感：选 WinUI 3。
- 想要最快看到视觉变化：选现代 WinForms。
- 这个项目既然目标是 Mica/Acrylic，建议直接走 WinUI 3。

## 阶段 3：现代 UX 范围

主计时窗口：

- 大号倒计时。
- 清晰显示当前模式：专注、短休息、长休息、空闲。
- Start、Pause、Stop 或 Interrupt 的主操作。
- 环形进度或紧凑时间线。
- Pin / always-on-top。
- 支持键盘和屏幕阅读器。

托盘体验：

- 左键打开计时窗口。
- 右键显示开始、停止、重置、设置、退出。
- 托盘图标反映当前模式或运行状态。
- Tooltip 显示剩余时间。

偏好设置：

- 专注、短休息、长休息时长。
- 长休息循环次数。
- 通知和声音。
- 开机启动。
- 主题：跟随系统、浅色、深色。
- 可选紧凑模式。

通知：

- 使用原生 Windows toast。
- 通知按钮支持开始休息、开始专注。
- 尊重系统通知设置。

视觉设计：

- 主窗口用 Mica，信息层保持清晰。
- 弹出层用 Acrylic，建立轻量层级。
- 支持浅色/深色主题。
- 高 DPI 正确。
- 对比度和焦点态合格。
- 小窗口靠近任务栏时位置自然，不挡核心操作。

## 阶段 4：打包和发布

- 用 `publish.ps1` 统一 ZIP、NSIS 和 MSIX 发布入口。
- Portable ZIP 作为免安装通道保留。
- NSIS installer 作为传统安装器通道保留，需要本机安装 NSIS。
- MSIX artifact 作为 Windows 11 现代分发通道保留，当前先产出 unsigned MSIX，正式分发前需要签名。
- 后续考虑代码签名，降低 SmartScreen 摩擦。
- 包装方案确定后，移除或替换旧版自动更新逻辑。

交付物：

- 可重复执行的 `publish.ps1` 和明确的 `dist\` 产物目录。

## 主要风险

- WinUI 3 需要 Windows App SDK 包和相应本地工具链。
- 当前 toast 通知包非常旧，应替换。
- 当前计时器基于 `System.Timers.Timer`，需要重新检查睡眠/唤醒、系统时间变化、UI 线程同步。
- 旧版自动更新逻辑可能不适合 MSIX。
- 现有 WinForms UI 和控件耦合较紧，不应当当作可复用 UI 资产。

## 第一批实施任务

1. 补充中文开发环境文档。
2. 迁移 `Tomighty.Core` 和 `Tomighty.Core.Test` 到 SDK-style。
3. 跑通 `dotnet test`。
4. 新建 WinUI 3 app shell spike，验证 Mica/Acrylic、托盘、通知是否可行。
5. 用现有 `PomodoroEngine` 接上第一版现代计时主界面。

## 待定决策

- 最低支持 Windows 版本。
- 是否只支持 Windows 11，还是兼容 Windows 10。
- 发布方式：MSIX、ZIP、传统安装器，或多种并存。
- 是否继续完整保留 Tomighty 名称和番茄图标，还是做轻度品牌刷新。
