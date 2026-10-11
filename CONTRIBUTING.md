# 开发说明

面向改这个插件的人。用户文档看 [README.md](README.md)。

## 结构

```
ClassIsland.Automation.Core   共享逻辑：AI 客户端、工具执行器、加密、TOTP、设置管理
ClassIsland.Automation.V1     1.x 外壳（net8.0-windows，WPF + MaterialDesign）
ClassIsland.Automation.V2     2.x 外壳（net8.0，Avalonia + FluentAvalonia）
```

业务逻辑都在 Core。V1/V2 只做界面和宿主适配，两边是**两套独立实现**，改一个功能通常要同时改两边。

新增一个 AI 工具要动两个地方：`AiToolDefinitions.GetAvailableTools` 里加定义，`ToolExecutor.Execute` 的 switch 里加分支。漏了任一处，模型要么看不到这个工具，要么调用时报「未知的工具名称」。

## 构建

**本机不需要装 .NET SDK。** 改完推 `main`，GitHub Actions 会编译两个目标并产出安装包。

```
gh run list --limit 3
gh run watch <run-id>
gh run download <run-id> -n ClassIsland-Automation-Plugins -D dist/ClassIsland-Automation-Plugins
```

打 tag（`1.0.0.0` 或 `v*`）会额外发 Release。

`.github/workflows/build.yml` 里打包步骤会把根目录的 `README.md` 一并塞进 `.cipx`，因为两个 `manifest.yml` 都写了 `readme: README.md`。

## 容易踩的坑

**`Name` 属性是代码契约。** XAML 里的 `Name="XxxYyy"` 会被 code-behind 按名字取，改了名不会报编译错，而是运行时控件为 null、那一块功能静默失效。改 XAML 时可以这样对比前后差异：

```powershell
function Names($t){ ([regex]::Matches($t,'Name="([A-Za-z0-9_]+)"') | % { $_.Groups[1].Value }) }
$old = (git show "HEAD:路径.xaml") -join "`n"
Compare-Object (Names $old) (Names (Get-Content -Raw 路径.xaml))
```

**WPF 和 Avalonia 的差异**：

| | V1 (WPF) | V2 (Avalonia) |
| --- | --- | --- |
| 显隐 | `Visibility="Visible"/"Collapsed"` | `IsVisible="True"/"False"` |
| 密码框 | `PasswordBox.Password` | `TextBox` + `PasswordChar` |
| 间距 | 子元素 `Margin`（`StackPanel` 无 `Spacing`） | `Spacing` |
| 网格 | 必须写 `<Grid.ColumnDefinitions>` | 可写 `ColumnDefinitions="*, Auto"` |
| `Thickness` | **只有 1 个和 4 个参数**，`new Thickness(8,3)` 编译报错 | 支持 2 个参数 |
| 主题画刷 | `MaterialDesignCardBackground`、`MaterialDesignDivider` 等 | `CardBackgroundFillColorDefaultBrush`、`SolidBackgroundFillColorBaseBrush` 等 |

**`CardBackgroundFillColorDefaultBrush` 是半透明的。** 铺在实心页面上没问题，但当浮层背景用会透出后面的字。弹窗、Toast 这类浮层要用 `SolidBackgroundFillColorBaseBrush`。

**配置对象只有一份。** 所有窗口都走 `SettingsManager.Current`，不要自己读盘反序列化，否则会和设置页各持一份、互相覆盖。宿主在 `Initialize` 里把路径注册给 `SettingsManager`。

**Core 里拿不到宿主类型。** `Plugin.Instance`、`IAppHost` 这些在 V1/V2 各自的程序集里，Core 直接引用会 CS0103。Core 需要宿主信息时，由宿主传进去。

## 提交

推送前先想清楚引用的类型在哪个程序集、`using` 齐不齐——这两个问题每次都要多花一轮 CI。
