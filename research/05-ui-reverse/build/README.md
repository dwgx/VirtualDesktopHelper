# build/ —— 02-shell.xaml 的编译验证工程

临时工程，唯一用途：证明 `../02-shell.xaml` 能被 WPF XAML 编译器接受。
**不进 `src/`**，可以整个删掉重造。

## 构建

```bash
cd D:/Project/VirtualDesktopHelper/research/05-ui-reverse/build
dotnet build ShellProof.csproj
```

期望输出末行：

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

干净重建：

```bash
dotnet clean -v quiet && dotnet build ShellProof.csproj
```

## 文件

| 文件 | 作用 |
|---|---|
| `ShellProof.csproj` | `net10.0-windows` + `UseWPF=true` + `OutputType=Library`，以 `<Page Include="..\02-shell.xaml">` **直接引用**上层真实文件（不复制副本） |
| `ShellWindow.CodeBehind.cs` | `x:Class` 对应的 partial class 桩，提供 `OnMinimizeClick` / `OnCloseClick` |

`OutputType=Library` 而非 `WinExe`：只需让 MSBuild 的 MarkupCompilePass 跑起来，不需要可执行入口。
**全程不启动任何 GUI 进程**，满足 Non-Conflict。

## 为什么 code-behind 叫 `.CodeBehind.cs` 而不是 `.g.cs`

.NET SDK 的默认 Compile glob **排除 `*.g.cs`**（那是 WPF 自己生成物的命名）。
命名成 `ShellWindow.g.cs` 会被静默忽略 → `InitializeComponent` 不存在 → `CS0103`。
手写 code-behind 必须避开 `.g.`。

## 命名空间必须与生成的 `.g.cs` 一致

`02-shell.xaml` 里 `x:Class="VDHelper.Streamer.ShellWindow"`，但 MSBuild 生成的
`obj/.../02-shell.g.cs` 里的类型是 `namespace VDHelper.Shell { class ShellWindow }`
—— 取决于 csproj 的 `RootNamespace`，**不是** `x:Class` 的值。

因此 `ShellWindow.CodeBehind.cs` 必须写 `namespace VDHelper.Shell`。
不一致时唯一症状是 code-behind 里调用 `InitializeComponent()` 那行报 `CS0103`，根因不明显。

验证命令：

```bash
grep -nE 'namespace|partial class' obj/Debug/net10.0-windows/02-shell.g.cs
```

## 确认真的编译了（而非空跑）

WPF 可能因为 glob 没匹配到文件而「0 error 通过」。两个检查：

```bash
ls -la obj/Debug/net10.0-windows/02-shell.baml   # 应存在，约 26.5 KB
```

以及产物程序集：

```bash
ls -la bin/Debug/net10.0-windows/VDHelper.ShellProof.dll
```

BAML 大小与 XAML 源码体积相当 → 整棵树都过了 BAML 序列化。

## 接入 src/ 时的注意事项

`02-shell.xaml` 刻意写成**零外部依赖**：

- 不引用官方程序集、不引用 `Xceed.Wpf.Toolkit`
- 不引用任何图片资源（logo / 平台图标用 `Border` 占位，尺寸按官方实测值）
- 转换器只用 WPF 内置 `BooleanToVisibilityConverter`

所以可以直接 `Content Include` 或 `<Page Include>` 进真实工程而不必先补资源。
落地时需要替换的只有：占位 `Border` → 真实 `Image`、`Text="—"` → 真实绑定、
`OnMinimizeClick`/`OnCloseClick` → 真实逻辑。