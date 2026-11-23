# MobaDesk 开发进度

> 记录日期：2026-11-19（含 2026-11-20 增补）
> 项目路径：`F:\Projects\WINUI3`
> 目标：用 WinUI 3 做一个类似 MobaXterm、**不限制并发 SSH 连接数**的多协议桌面客户端，支持 SSH / SFTP / FTP(FTPS) / RDP / Linux 服务器监控，并具备**可切换窗口材质、主题与多语言（均即时生效）**。
> 当日总结（2026-11-20 全部改动一览）见 **第 19 节**。

---

## 1. 今日总体进展

| 阶段 | 内容 | 状态 |
| --- | --- | --- |
| 环境与可行性验证 | 探测 .NET / Windows App SDK / WebView2，搭建最小 WinUI 3 非打包自包含工程并跑通 | ✅ |
| 主体功能开发 | SSH 终端、SFTP、FTP/FTPS、RDP、监控、会话管理、凭据加密 | ✅ |
| 主题 / 材质 / 多语言 | Mica / MicaAlt / Acrylic / 纯色，System / Dark / Light，zh-CN / en-US，全部热切换 | ✅ |
| UI 缺陷修复 | 标签页内容不拉伸导致终端只有 7 行；关闭按钮提示未本地化等 | ✅ |
| 会话文件夹与拖拽归类 | 侧栏文件夹、新建/重命名/删除/折叠、拖拽到文件夹或根目录、持久化 | ✅ |
| 验证 | 编译 0 警告 0 错误、服务层端到端探针全通过、UI 自动化验证、Release 发布启动验证 | ✅ |

---

## 2. 交付的功能

### 2.1 SSH 终端
- WebView2 + xterm.js 5.5.0（FitAddon 0.10.0）实现完整终端仿真：ANSI 颜色、回滚、光标、复制粘贴。
- C# ↔ JS 通过 JSON 消息（base64 负载）通信：`ready` / `input` / `resize` / `copy` / `paste`。
- 输出每 24 ms 批量写入，4 MB 背压保护，避免高频输出卡死 UI。
- 认证：密码、私钥（含口令）、键盘交互式；主机密钥指纹显示在标签页状态栏。
- 快捷键：`Ctrl+T` 新建 SSH、`Ctrl+W` 关闭标签页、`Ctrl+Shift+C/V` 复制粘贴。

### 2.2 SFTP / FTP / FTPS
- 每个 SSH 标签页内置 440px 宽的 SFTP 侧栏：浏览、上传、下载（含递归下载目录）、新建目录、重命名、删除。
- 独立的 FTP / FTPS 会话标签页（显式 TLS）；FluentFTP 实现。
- 传输进度条 + 状态栏文字，全部走异步、可取消。

### 2.3 RDP
- 生成 `.rdp` 文件并调用系统 `mstsc`，支持 NLA、分辨率、全屏、剪贴板与本地磁盘重定向。
- 密码以 DPAPI 密文写入 `password 51:b:` 字段，仅当前 Windows 用户可解密。

### 2.4 服务器监控
- SSH 标签页内置监控面板：CPU、内存、Swap、负载、网络吞吐、磁盘、运行时间、Top CPU 进程。
- 单条 SSH 命令按 `@@STAT/@@MEM/...` 分段采集 `/proc` 数据，一次连接取全量快照（实测 ≈430 ms）。
- 自定义 Sparkline 控件绘制 CPU / 网络实时曲线。

### 2.5 会话管理与文件夹（本轮新增）
- 侧栏结构：**根目录会话** + **文件夹（可折叠，显示数量徽标）**。
- 文件夹操作：
  - 新建：`新建 → 新建文件夹`
  - 重命名：右键文件夹 → 重命名（同步更新文件夹内会话的分组）
  - 删除：右键 → 删除（会话**移回根目录**，不删除会话）
  - 折叠/展开：点击箭头，或双击/回车文件夹行
- 拖拽归类：
  - 会话拖到文件夹行 → 放入该文件夹（自动展开）
  - 会话拖到列表空白处 / 侧栏头部 → 回到根目录
  - 拖动时显示 “移动到 xxx / 移动到根目录” 的操作提示
  - 文件夹行本身不可拖动
- 右键会话新增 **移动到** 子菜单（根目录 + 所有文件夹），作为拖拽的等价入口。
- 搜索：可匹配文件夹名，命中文件夹名时显示其中全部会话。
- 持久化：`profiles.json`（`Group` 字段）+ 新增 `folders.json`（文件夹顺序）；启动时自动为历史 `Group` 补建文件夹。

### 2.6 外观与本地化（即时生效）
- 窗口材质：`Mica` / `MicaAlt` / `Acrylic` / `Solid`，不可用时自动回退并提示。
- 主题：`System` / `Dark` / `Light`，同时同步 DWM 标题栏与内容主题。
- 多语言：`zh-CN` / `en-US`，约 260 条词条；设置对话框内改动立即预览，取消则还原。
- 实现方式：
  - `Localization.T(key[, args])` 静态词表 + `LanguageChanged` 事件。
  - `loc:Loc.Key/Header/Placeholder/ToolTip` 附加属性绑定 XAML 文本，`Loc.ApplyTree()` 就地刷新，无需重建窗口。
  - 各视图保存“状态键”，语言切换时重新取词。

---

## 3. 修复的关键问题

| 问题 | 现象 | 原因 | 处理 |
| --- | --- | --- | --- |
| 标签页内容不拉伸 | SSH 终端只有 938×142（7 行），大量空白，点击/键盘输入落空 | WinUI `TabView` 默认 `VerticalAlignment=Top`，内容按期望尺寸收缩 | `TabView` 显式 `Horizontal/VerticalAlignment=Stretch`，`TabViewItem` 同时设置 `*ContentAlignment=Stretch` |
| 发布版启动崩溃 | `XamlParseException`（找不到 `MobaDesk.pri`） | `dotnet publish` 未复制 PRI | csproj 增加 `CopyPriToPublish`（AfterTargets=Publish） |
| 拖拽落点判断错误 | 会话总被移动到根目录 | `DragEventArgs.OriginalSource` 是接收事件的控件（ListView），不是指针下的项 | 改为按 `e.GetPosition(SessionsList)` 命中测试 `ListViewItem` 容器 |
| 关闭标签页提示语言不一致 | 英文界面出现中文 “关闭标签页” | WinUI 模板资源跟随系统语言 | 找到 `CloseButton`，设置本地化 `ToolTip` + `AutomationProperties.Name` |
| 会话分组写入中文字面量 | 空分组被写成“默认分组” | 模型层默认值硬编码 | 模型改为空串，对话框保存时归一化，显示层按当前语言处理 |
| 监控数据解析失败 | 采集结果为空 | SSH 命令脚本含 CRLF，Linux 侧产生 `null\r` 文件 | 发送前 `\r\n → \n` 归一化 |

---

## 4. 验证结果

### 4.1 构建

```powershell
dotnet build .\src\MobaDesk\MobaDesk.csproj -c Debug     # 0 警告 0 错误
dotnet publish .\src\MobaDesk\MobaDesk.csproj -c Release -r win-x64 --self-contained true -o .\publish\win-x64
```
- 发布产物自带 .NET 与 Windows App SDK 运行时，`MobaDesk.pri`、`Assets`、xterm.js 均齐全，可直接运行。

### 4.2 服务层端到端探针（真实服务）

`tests/MobaDesk.ServiceProbe` 对 WSL 中的 Debian 13 sshd（127.0.0.1:2222）与 vsftpd（127.0.0.1:2121）实测：

```
PASS: interactive shell roundtrip.
PASS: SFTP upload/download.
PASS: FTP upload/download.
PASS: RDP file generation.
PASS: monitor snapshot in 432ms.
ALL TESTS PASSED
```

### 4.3 UI 自动化验证（UIA + 模拟鼠标拖拽）

- 多标签 SSH / FTP 会话、SFTP 面板（英文 Name/Size/Modified）、监控面板（Disks / Top CPU processes）。
- 主题 / 材质 / 语言热切换，`settings.json` 落盘正确；已打开标签页即时刷新。
- 文件夹：创建、折叠/展开、重命名（同步 `profiles.json` 分组）、删除（会话回到根目录）。
- 拖拽：会话拖入文件夹后 `profiles.json` 的 `Group` 更新且侧栏归位；拖回根目录后 `Group` 为空。
- Release 发布版启动截图确认英文界面、Mica 深色材质正常。

---

## 5. 技术栈与工程结构

- **UI**：WinUI 3（Windows App SDK 1.8.260921001），非打包、自包含、`win-x64`。
- **运行时**：.NET 8（`net8.0-windows10.0.19041.0`）。
- **依赖**：SSH.NET 2026.0.1、FluentFTP 55.1.0、CommunityToolkit.Mvvm 8.4.2、System.Security.Cryptography.ProtectedData 10.0.0。
- **数据目录**：`%LOCALAPPDATA%\MobaDesk`，可用 `MOBADESK_DATA_DIR` 覆盖（便携模式/多实例）。

```
src/MobaDesk/
  Models/           ConnectionProfile、ServerMetrics、AppSettings、RemoteEntry
  Services/         SshSession、SshAuth、MonitorService、ProfileStore、Localization、ThemeService、
                    RdpLauncher、CredentialProtector、AppPaths(Log)
  Services/FileSystems/  IRemoteFileSystem、SftpRemoteFileSystem、FtpRemoteFileSystem
  Localization/Loc.cs    XAML 本地化附加属性
  Views/            SshTerminalView、FileBrowserView、MonitorView、RdpView、SidebarRow
  Dialogs/          ProfileDialog、PasswordDialog、SettingsDialog
  Controls/         Sparkline
  Assets/           terminal.html、xterm/、app.ico、logo.png
tests/MobaDesk.ServiceProbe/   服务层端到端探针
```

数据文件：

| 文件 | 说明 |
| --- | --- |
| `profiles.json` | 会话配置（密码为 DPAPI 密文） |
| `folders.json` | 会话文件夹（有序字符串数组） |
| `settings.json` | 主题 / 材质 / 语言 / 终端与监控参数 |
| `logs/app.log` | 运行日志（超过 2 MB 轮转） |

---

## 6. 已知限制

1. 服务器监控仅支持 Linux（依赖 `/proc`、`df`、`ps`）。
2. RDP 会话运行在独立的 `mstsc` 窗口中，未内嵌到标签页。
3. SFTP / 监控面板各自建立一条额外 SSH 连接。
4. 会话文件夹为单层结构，不支持嵌套；文件夹不支持拖动排序。
5. 文件夹折叠状态不持久化（重启后全部展开）。
6. 终端已输出的历史内容不随语言切换重绘（界面文字会即时切换）。

---

## 7. 后续可做

- 文件夹嵌套、拖动排序、折叠状态持久化。
- 会话 / 文件夹批量导入导出（MobaXterm、PuTTY 会话导入）。
- SFTP 面板拖拽上传、断点续传、权限（chmod）编辑。
- 命令面板 / 全局快捷搜索、会话多窗口、SSH 隧道（端口转发）。
- 监控告警阈值、历史曲线落盘。

---

## 8. 2026-11-20 增补

### 8.1 细节打磨

| 项目 | 说明 |
| --- | --- |
| 移除数字输入框的上下箭头 | 所有 `NumberBox` 的 `SpinButtonPlacementMode` 由 `Compact` 改为 `Hidden`（设置、会话编辑、RDP 面板共 10 处）：输入框聚焦/悬停不再浮出微调箭头，只保留清除按钮 |

### 8.2 发布体积优化（215 MB → 166 MB）

Windows App SDK 1.8 自包含发布会附带整套 Windows AI 运行时与 86 个语言资源目录，
MobaDesk 完全用不到。新增 `SlimPublishPayload` MSBuild 目标（AfterTargets=Publish）自动删除：

- Windows AI / 本地模型栈：`onnxruntime*.dll`、`DirectML.dll`、`Microsoft.ML.OnnxRuntime.dll`、
  `Microsoft.Windows.AI.*`、`Microsoft.Windows.Vision*`、`Microsoft.Windows.Workloads*`、
  `NpuDetect*`、`workloads*.json`（≈ 47 MB）
- Widgets / PushNotifications 投影（≈ 2.5 MB）
- 除 `en-us`、`zh-CN` 外的 84 个语言资源目录（≈ 3.2 MB）
- `*.pdb` 符号文件

可用 `-p:MobaDeskSlimPayload=false` 关闭。裁剪后启动、SSH 连接、设置读写、侧栏文件夹均实测正常。

### 8.3 发布模式

新增 `publish.ps1`，一条命令产出并打印体积：

| 模式 | 命令 | 体积 | 目标机要求 |
| --- | --- | --- | --- |
| 自包含精简（默认） | `.\publish.ps1` | 166.1 MB / 304 文件 | 无 |
| 依赖运行时 | `.\publish.ps1 -Fx` | 43.1 MB / 49 文件 | .NET 8 Desktop Runtime + Windows App Runtime 1.8 |
| 自包含 + ReadyToRun | `.\publish.ps1 -R2R` | 219.6 MB | 无（启动更快） |
| Native AOT | `.\publish.ps1 -Aot` | — | MSVC 链接器 + WinUI 官方尚不支持 |

### 8.4 Native AOT 支持（已验证：可编译链接，但 WinUI 下无法运行）

- csproj 新增 `MobaDeskNativeAot` 属性组：`PublishAot`、`OptimizationPreference=Size`、
  `StackTraceSupport=false`、`EventSourceSupport=false`、`UseSystemResourceKeys=true`、`IsAotCompatible=true`、
  `CopyOutputSymbolsToPublishDirectory=false`（AOT 的原生 pdb 有 77 MB，不需要随包分发）。
- 代码按 AOT 要求改造：
  - 新增 `Models/MobaDeskJsonContext.cs`（`JsonSerializerContext` 源生成），
    `AppSettings`、`List<ConnectionProfile>`、`List<string>`、`string`、`TerminalConfigureOptions` 全部静态生成。
  - **所有 JSON 调用改为 `JsonTypeInfo` 重载**（`MobaDeskJsonContext.Default.AppSettings` 等）。
    原先 `JsonSerializer.Serialize(value, options)` 泛型重载会触发 12 条 IL2026 / IL3050，现已 **0 告警**。
  - 终端配置消息由匿名类型序列化改为强类型 + 源生成；`window.term.writeText` 的字符串转义改用源生成序列化器。
  - `Sparkline` 标记为 `partial`，消除 CsWinRT1028 告警。
- `publish.ps1 -Aot` 增加**链接器预检**：`vswhere` / `PATH` 查找 `link.exe`，缺失时输出中文安装指引
  （`winget install --id Microsoft.VisualStudio.2022.BuildTools -e` + 「使用 C++ 的桌面开发」）并以退出码 1 结束；
  `-Force` 跳过预检。找到链接器后自动把 `…\Microsoft Visual Studio\Installer` 前置到 `PATH`，
  否则 ILC 的 `findvcvarsall.bat` 里 `vswhere.exe` 解析失败，会把错误文本拼进链接命令（`MSB3073`）。
- 环境准备：本机已通过 winget 安装 VS 2022 生成工具（VCTools 工作负载，MSVC 14.44 + Windows SDK 10.0.26100），
  链接器位于 `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\14.44.35207\bin\Hostx64\x64\link.exe`。
- **实测结果**：`publish.ps1 -Aot` 成功产出 66.7 MB / 87 文件（`MobaDesk.exe` 12.79 MB），
  但启动即崩溃，退出码 `0xC000027B`：

  ```
  System.ArgumentException: Arg_ArgumentException
     at WinRT.ExceptionHelpers.<ThrowExceptionForHR>g__Throw|38_0(Int32)
     at ABI.Microsoft.UI.Xaml.Controls.IItemsControlMethods.set_ItemsSource(IObjectReference, Object)
     at MobaDesk.MainWindow..ctor()
     at MobaDesk.App.OnLaunched(LaunchActivatedEventArgs)
  ```

  根因：CsWinRT 将托管集合（`ObservableCollection<T>`）封送为 WinRT 集合以赋给
  `ItemsControl.ItemsSource` 时需要运行时代码生成，AOT/裁剪下不可用。
  同一处崩溃在 `PublishTrimmed` 实验中也复现，说明这是 WinUI 3 的结构性限制。
  结论：AOT 通道保留（编译、链接、体积数据齐全），但**不可用于发布**；等 Windows App SDK
  官方支持后再评估。

### 8.5 已验证不可用的方案

| 方案 | 结果 |
| --- | --- |
| `PublishTrimmed=true` + `TrimMode=partial` | 体积可降到 81.8 MB，但启动即崩溃（`WinRT.MarshalInspectable.CreateMarshaler2`，CsWinRT 封送代码被裁掉）。加 `TrimmerRootAssembly=Microsoft.WinUI` 后仍崩溃，确认 WinUI 3 不能裁剪 |
| Native AOT（`-Aot`） | 可编译、可链接、66.7 MB，但启动崩溃于 `ItemsControl.set_ItemsSource` 的 CsWinRT 集合封送（同上根因），确认不可用于发布 |
| 单文件 `PublishSingleFile` | WinUI 打包依赖磁盘上的原生 DLL 与 PRI，不适用，未采用 |

### 8.6 验证

- `publish.ps1` 四种模式均构建成功：自包含精简 166.1 MB / 304 文件、依赖运行时 43.1 MB / 49 文件、
  ReadyToRun 219.6 MB、Native AOT 66.7 MB / 87 文件。
- 自包含精简版与依赖运行时版均启动成功并实测可用；AOT 版链接成功但启动崩溃（见 §8.4）。
- 精简版实测：`settings.json` 读写、`profiles.json`/`folders.json` 反序列化、SSH 连接、xterm.js 终端渲染全部正常。
- JSON 源生成改造后回归测试：以 `"Protocol": "Ssh"` / `"Auth": "Password"` 种子数据启动，侧栏正确解析文件夹与会话；
  通过右键「移动到」改分组后回写 `profiles.json`，枚举仍写作 `"Ssh"`、缩进保持、字段完整。
- 服务层探针（SSH / SFTP / FTP / RDP / 监控）在 JSON 源生成改造后仍为 `ALL TESTS PASSED`。
- Debug / Release 构建 0 警告 0 错误；`-p:MobaDeskNativeAot=true` 下 AOT 分析器告警同样为 0。
- `publish.ps1 -Aot` 预检在 PowerShell 5.1 与 PowerShell 7 下均正确输出中文指引并退出（脚本以 UTF-8 BOM 保存，
  否则 PS 5.1 会把中文按 ANSI 解析导致语法错误）。

---

## 9. 2026-11-20 增补：并发会话

### 9.1 功能

| 需求 | 实现 |
| --- | --- |
| 多选会话 | 侧栏 `ListView` 改为 `SelectionMode="Extended"`，支持 Ctrl / Shift / Ctrl+A 多选，底部提示同步更新 |
| 同时打开多个窗口 | 双击任意已选会话、回车、或右键「打开所选会话 (N)」都会打开所有选中会话；多个 SSH 会话合并到同一个「并发会话」标签页，按每行两列自动排布，多余的进入下一行，最后一行只剩一个时铺满整行（每个 SSH 会话仍按需弹密码框） |
| 同时发送命令 | 标题栏新增「并发输入」开关；开启后顶部出现命令栏，回车或「发送」可把一条命令下发到所有参与并发的 SSH 会话 |
| 键击镜像 | 在任一参与并发的终端里键入，输入会镜像到其它参与并发的会话（`SshTerminalView.UserInput` 事件 → `WriteBroadcast`） |
| 临时排除 | 每个终端左下角新增「参与并发」复选框（默认勾选），取消勾选即临时排除；标题栏实时显示「N 个会话参与」 |

### 9.2 关键实现

- `SshTerminalView`：
  - 新增 `UserInput`（键入）与 `BroadcastMembershipChanged`（勾选变化）事件，`IsBroadcastMember` 暴露勾选状态。
  - `WriteBroadcast(byte[])` 直接写入会话（不本地回显，由远端回显）；`SendCommandLine(string)` 追加 `\r` 执行。
  - 输入框内容改为：`var bytes = Convert.FromBase64String(base64); _session.Write(bytes); UserInput?.Invoke(this, bytes);`
- `MainWindow`：
  - `TerminalViews()` 遍历标签页取 `SshTerminalView`；`OnTerminalUserInput` 按 `_broadcastOn && source.IsBroadcastMember`
    过滤，再写入其它 `IsBroadcastMember` 的会话。
  - `SetBroadcast(bool)` 控制命令栏可见性、状态栏提示与计数；关闭最后一个标签页时自动关闭并发输入。
  - 多选打开：`ProfilesToOpen()` 会在「多选被鼠标单击折叠为单选」的 4 秒窗口内复用上次多选快照
    （WinUI ListView 在双击/右击时会先把选择折叠成单击项，`SelectionChanged` 里缓存多选即可解决）。
- 新增 `ToolbarToggleButtonStyle`（App.xaml）：`ToolbarButtonStyle` 的 TargetType 是 `Button`，不能套在 `ToggleButton` 上。
- 本地化新增 16 个词条（`Broadcast.*`、`Ssh.BroadcastMember*`、`Sidebar.OpenSelected/HintMultiSelect/OpeningMany`）。

### 9.3 顺带修复的问题

| 问题 | 现象 | 处理 |
| --- | --- | --- |
| 点击终端无法获得键盘焦点 | 通过脚本点击终端区域后键入无效（只有连接完成时 `FocusTerminal()` 自动获得过焦点） | 终端容器 `Grid` 增加 `PointerPressed` / `Tapped`，调用 `TerminalWeb.Focus()` + `window.term.focus()`；勾选「参与并发」后也把焦点还给终端 |

### 9.4 验证（3 个真实 SSH 会话，WSL Debian）

```
多选 3 个会话 -> 双击 -> tabs=3 (WSL-A / WSL-B / WSL-C)
开启并发输入 -> 计数显示「3 个会话参与」
取消 WSL-C 的「参与并发」 -> 计数变为「2 个会话参与」
命令栏下发 "echo bc >> /tmp/bc_lines" -> 远端文件 2 行（被排除的会话未执行）
重新勾选 -> 键入 "echo mirrored >> /tmp/bc_keys" -> 远端文件 3 行（本地 + 2 个镜像）
排除状态下键入 "echo excluded >> /tmp/bc_keys" -> 只产生 1 行（无镜像）
```

- 中英双语界面均验证：`Type a command and press Enter to send it to every participating session`、
  `2 session(s) in broadcast`、`In broadcast`、状态栏 `Broadcast enabled`。
- Debug 构建 0 警告 0 错误。

---

## 10. 2026-11-20 增补：并发会话自动排布

### 10.1 需求

多选多个 SSH 会话时，不再为每个会话单独开一个标签页，而是在一个「并发会话」标签页里自动排布：
每行两列，多余的会话进入下一行；最后一行只剩一个时铺满整行。

### 10.2 实现

- 新增 `Views/ConcurrentTerminalView`：一个只负责排布的容器。
  - `Add(profile, terminal)` 把 `SshTerminalView` 包进 `ConcurrentPane`。
  - `Relayout()` 按 `ceil(n / 2)` 动态创建等宽列与等高行，逐个设置 `Grid.Row` / `Grid.Column`；
    当 `n % 2 == 1` 时最后一格 `ColumnSpan = 2`，即「多余的下一行自铺满」。
  - 实现 `IAsyncDisposable`：关闭标签页时依次释放所有终端。
  - `PanesChanged` 事件让主窗口刷新会话计数；`Empty` 事件在最后一个窗格关闭后关闭标签页。
- 新增 `Views/ConcurrentPane`：每个窗格顶部的精简标题栏（会话名、`user@host:port`、关闭按钮），
  下方承载原有 `SshTerminalView`。
- `MainWindow`：
  - `OpenSelectedAsync()`：选中的 SSH 会话 ≥ 2 时调用 `OpenConcurrentAsync()`；其它协议（SFTP / FTP / RDP）仍按原逻辑逐个打开。
  - `OpenConcurrentAsync()`：依次获取凭据（缺失时仍弹密码框），把成功的会话装入同一个
    `ConcurrentTerminalView`，创建标题为「并发会话」的标签页后并行执行 `StartAsync()`。
  - `TerminalViews()` 改为展开 `ConcurrentTerminalView.Terminals`，使并发输入、命令下发与计数覆盖同一标签页内的所有终端。
  - 状态栏会话数改为按窗格计数，而不是按标签页计数。
  - 编辑会话名称后同步刷新并发窗格标题；切换语言时同步刷新「并发会话」标签页标题。
- 输入同步：`OnTerminalUserInput` 先定位来源窗格所在的 `ConcurrentTerminalView`。属于并发标签页时，
  即使全局「并发输入」开关未开启，也会把键入内容实时镜像给同标签页内勾选「参与并发」的其它窗格；
  全局开关开启时按原逻辑镜像到所有参与会话，额外覆盖其它标签页。
- 粘贴同步：`SshTerminalView` 的 `paste` 消息此前直接调用 `_session.WriteText`，绕过了 `UserInput`，
  导致 `Ctrl+Shift+V` 粘贴不会镜像；现在改为写入当前会话后同样触发 `UserInput`，
  与键入共用同一条并发镜像路径（`OnTerminalUserInput` → `WriteBroadcast`）。
- 本地化新增 `Tab.Concurrent`、`Sidebar.OpenedMany` 两个词条（中英双语）。

### 10.3 验证

- `dotnet build src/MobaDesk/MobaDesk.csproj -c Debug -p:Platform=x64`：0 警告 0 错误。
- 排布规则（`n` 为选中的 SSH 会话数）：
  - `n=2`：1 行 × 2 列；
  - `n=3`：第 1 行 2 个，第 2 行 1 个且跨两列；
  - `n=5`：2 + 2 + 1（最后一个跨两列）；
  - `n=6`：3 行 × 2 列。
---

## 11. 2026-11-20 增补：文件权限列与右键菜单

### 11.1 功能

| 需求 | 实现 |
| --- | --- |
| 权限列 | 文件列表新增「权限」列，按 Unix 模式显示 `drwxr-xr-x`、`-rw-r--r--` 或 `lrwxrwxrwx`；服务器不返回权限时显示 `—` |
| 文件右键菜单 | 下载、重命名、复制路径、刷新、删除 |
| 文件夹右键菜单 | 打开、下载、上传到此处、在此新建文件夹、重命名、复制路径、刷新、删除 |
| 多选右键菜单 | 下载、复制路径、刷新、删除（重命名和文件夹专属操作在单选时显示） |
| 空白处右键菜单 | 上传到当前目录、在当前目录新建文件夹、刷新 |

### 11.2 关键实现

- `RemoteEntry` 新增 `UnixMode`（0x0000–0x0FFF，-1 表示未知）与 `PermissionsText`。
- `FormatUtil` 新增：
  - `Permissions(mode, isDirectory, isSymlink)`：生成标准 10 位权限串，并处理 setuid / setgid / sticky（`s`/`S`、`t`/`T`）。
  - `ModeFromSymbolic(string)`：解析 `rwxr-xr-x` 形式的权限串，供 FTP 列表使用。
- SFTP：由 `SftpFileAttributes` 的 `OwnerCanRead/Writer/Execute` 等布尔值和 `IsUIDBitSet` / `IsGroupIDBitSet` / `IsStickyBitSet` 组装 Unix 模式。
- FTP：优先解析 `FtpListItem.RawPermissions`，否则回退到 `Chmod`。
- `FileBrowserView`：
  - 列表头与行模板新增权限列，列宽为 22 / * / 92 / 76 / 108。
  - `RightTapped` 先把右键目标切换为当前选择；`MenuFlyout.Opening` 根据单选/多选/文件/文件夹动态显示菜单项。
  - 抽出 `DownloadSelectedAsync`、`UploadAsync(targetDirectory)`、`DeleteSelectedAsync`、`RenameSelectedAsync`、`CreateFolderAsync(parentPath)`，工具栏与右键菜单共用同一套逻辑。
  - 「复制路径」把所选条目的完整路径按行写入剪贴板，并在状态栏显示已复制数量。
- 本地化新增 `File.Permissions/Open/UploadHere/NewFolderHere/CopyPath/PathsCopied`。

### 11.3 验证

- `dotnet build src/MobaDesk/MobaDesk.csproj -c Debug -p:Platform=x64`：0 警告 0 错误。
- 权限格式化覆盖：普通文件、目录、符号链接、setuid/setgid/sticky，以及 FTP 无权限信息时的 `—` 回退。

---

## 12. 2026-11-20 增补：远程文件编辑器

### 12.1 功能

| 需求 | 实现 |
| --- | --- |
| 双击文件编辑 | 文件列表双击文件时在主窗口新建 `readme.txt` 之类的编辑标签页；文件夹双击仍按原逻辑进入目录 |
| 右键编辑 | 文件右键菜单新增「编辑」，与双击等效 |
| 读写权限判断 | 打开文件时结合权限位与真实的远端写权限探测；当前账号不可写时进入只读模式 |
| 只读标识 | 编辑器左下角显示锁形图标 + 「只读」，保存按钮禁用，并带「当前账号对该文件没有写权限」提示 |
| 保存 | 保存按钮与 `Ctrl+S`，保存成功清除标题上的 `*`；失败时在 InfoBar 显示原因并重新探测权限 |
| 关闭保护 | 有未保存更改时，关闭标签页会弹出「保存 / 放弃更改 / 取消」确认 |

### 12.2 关键实现

- `IRemoteFileSystem` 新增 `ReadFileAsync`、`WriteFileAsync`、`CanWriteAsync`。
  - SFTP：`OpenRead` / `OpenWrite` 读写；写权限探测使用 `Open(path, FileMode.Open, FileAccess.Write)`，
    只发送 `SSH_FXF_WRITE`（不带 truncate），不会破坏文件内容；服务端拒绝时返回 `false`。
  - FTP：`DownloadStream` / `UploadBytes` 读写；写权限探测使用 `APPE` 打开已有文件后立即关闭，
    `550/532/553` 视为无权限，其它异常返回“未知”，由权限位兜底。
- 新增 `Views/RemoteEditorView`：
  - 多行等宽 `TextBox`、保存 / 刷新按钮、InfoBar 错误提示、底部状态栏。
  - 编码识别：UTF-8（含/不含 BOM）、UTF-16 LE/BE；保存时沿用原编码与 BOM。
  - 二进制与超大文件保护：超过 2 MB 或含 NUL 字节的文件不进入编辑模式。
  - 换行保留：加载时识别原文件的 `\n` / `\r\n` / `\r`，保存时把 TextBox 的 `\r` 还原为原风格。
  - 脏标记：与加载时的规范化文本比较，标题追加 `*`；支持 `Ctrl+S`。
- `FileBrowserView`：新增 `DoubleTapped` 处理与「编辑」菜单项，调用 `App.Shell.OpenRemoteFileEditor`。
- `MainWindow`：新增 `OpenRemoteFileEditor`、`RequestCloseTabAsync`
  （`Ctrl+W` 与标签页关闭按钮都会先执行编辑器的未保存确认）。

### 12.3 验证（本地 FTP：可写账号 / 只读账号）

- 只读账号双击 `readme.txt`：编辑器正常加载，左下角显示锁图标与「只读」，保存按钮禁用。
- 可写账号双击同一文件：标题无 `*`，保存按钮可用；修改内容后通过 UIA 保存，
  远端文件内容更新且保留原有 `\r\n` 换行。
- `dotnet build src/MobaDesk/MobaDesk.csproj -c Debug -p:Platform=x64`：0 警告 0 错误。

---

## 13. 2026-11-20 增补：文件类型彩色图标

### 13.1 功能

文件列表不再统一使用灰色图标：根据条目类型 / 扩展名自动选择图标颜色，
文件夹、脚本、配置、文档、压缩包、图片、视频、音频、二进制文件一眼可区分。

### 13.2 颜色规则

| 类型 | 颜色 | 示例扩展名 |
| --- | --- | --- |
| 文件夹 | 琥珀 `#FBBF24` | `logs/`、`apparmor/` |
| 符号链接 | 青色 `#22D3EE` | 任意 symlink |
| 代码 / 脚本 | 绿色 `#34D399` | `cs js ts py go sh ps1`，以及无扩展名的 dotfile |
| 配置 / 数据 | 橙色 `#FB923C` | `json xml yaml toml ini conf`、`Dockerfile`、`Makefile` |
| 文档 | 蓝色 `#60A5FA` | `txt md log pdf csv docx` |
| 压缩包 | 紫色 `#A78BFA` | `zip rar 7z tar gz` |
| 图片 | 粉色 `#F472B6` | `png jpg gif svg webp` |
| 视频 | 红色 `#F87171` | `mp4 mkv avi mov` |
| 音频 | 品红 `#E879F9` | `mp3 wav flac ogg` |
| 可执行 / 二进制 | 蓝绿 `#2DD4BF` | `exe dll so bin jar` |
| 其它 | 灰蓝 `#94A3B8` | 无法归类的文件 |

### 13.3 实现

- 新增 `Models/FileIconPalette`：纯函数 `ColorFor(name, isDirectory, isSymlink)` 做类型判断，
  缓存每种颜色对应的 `SolidColorBrush`，避免每个列表项重复创建画刷。
- `RemoteEntry` 新增 `IconColorHex` / `IconBrush`；`IconBrush` 在 UI 线程通过 `x:Bind` 取值，
  避免在后台列举目录时创建线程相关的 XAML 对象。
- `FileBrowserView` 的 `FontIcon.Foreground` 改为 `{x:Bind IconBrush}`，图标字号从 14 调到 15。

### 13.4 验证

- 本地 FTP 列表包含 `logs/`、`.bashrc`、`app.py`、`archive.zip`、`data.json`、`movie.mp4`、
  `notes.txt`、`photo.png`、`readme.md`、`script.sh`、`song.mp3`、`tool.exe`：
  实际截图确认分别显示琥珀、绿、绿、紫、橙、红、蓝、粉、蓝、绿、品红、蓝绿。
- `dotnet build src/MobaDesk/MobaDesk.csproj -c Debug -p:Platform=x64`：0 警告 0 错误。

---

## 14. 2026-11-20 修复：并发文字颜色与可拖动分隔条

### 14.1 修复内容

| 问题 | 处理 |
| --- | --- |
| 终端左下角「参与并发」文字呈青色，与深色背景对比异常 | WinUI `CheckBox` 选中时使用 `CheckBoxForegroundChecked` 主题资源覆盖了 `Foreground`。改为把 `TextBlock` 放进 `CheckBox.Content`，由 `TextBlock` 固定使用 `#D7DDE8`，不再受选中状态影响 |
| 并发标签页内两个 SSH 窗格之间的边框不能拖动 | 新增 `Controls/VerticalResizeHandle`，在并发布局中插入 8px 可拖动分隔列；拖动时按指针位置实时调整左右列星号比例（20%–80%） |
| SSH 右侧 SFTP / 监控面板不能拖动 | `SshTerminalView` 的终端与侧栏之间插入同一个拖动控件；拖动时调整 `SidePanel.Width`（最小 280，最大为容器宽度的 80%），面板打开/关闭时同步显示/隐藏分隔条 |

### 14.2 实现细节

- `VerticalResizeHandle` 继承 `Grid`，透明背景 + 1px 中线，鼠标悬停显示左右拖动光标
  （`InputSystemCursorShape.SizeWestEast`）。
- 使用指针捕获（`CapturePointer`）保证拖出 8px 区域后仍能持续收到移动事件；
  事件回调携带相对于父容器的指针 X 坐标，父容器直接计算新比例/宽度，避免累计误差。
- 并发布局由 2 列改为「星号列 / 8px 分隔列 / 星号列」；只剩一个窗格时该窗格跨 3 列并隐藏分隔条。
- 本地化无需新增词条。

### 14.3 验证

- 并发标签页拖动中间分隔条：左侧窗格变宽、右侧变窄，比例保持在 20%–80%。
- 打开 SFTP 侧栏后拖动左边缘：侧栏宽度实时变化，关闭后分隔条隐藏。
- 三个终端左下角的「参与并发」文字均为浅灰色，选中状态下不再变青。
- `dotnet build src/MobaDesk/MobaDesk.csproj -c Debug -p:Platform=x64`：0 警告 0 错误。

---

## 15. 2026-11-20 增补：终端右键菜单

### 15.1 功能

在 SSH 终端区域右键会打开上下文菜单：

| 菜单项 | 行为 |
| --- | --- |
| 复制 (Ctrl+Shift+C) | 复制 xterm 当前选区；无选区时禁用 |
| 粘贴 (Ctrl+Shift+V) | 读取系统剪贴板并写入会话，同时保留并发镜像 |
| 查找 (Ctrl+Shift+F) | 打开终端右上角的查找栏，支持上一个 / 下一个匹配 |
| 清屏 (Ctrl+Shift+L) | 清空终端可视区与回滚缓冲 |
| 重新连接终端 | 重新建立当前 SSH 会话 |
| 选中文本 > | 全选 / 取消选择 |
| 布局 > | 水平分屏 / 垂直分屏，在新标签页中打开同一会话的两个终端（上下 / 左右） |
| 更多操作 > | 打开 SFTP 面板 / 监控面板 |

### 15.2 实现

- `terminal.html`：
  - 监听 `contextmenu` 并通过 `postMessage` 上报 `{type:'contextmenu', x, y}`；
  - `onSelectionChange` 始终上报 `hasSelection`，供菜单控制「复制」是否可用；
  - 新增 `term.findNext(query, backwards)`：遍历 xterm 缓冲区，选中并滚动到匹配行，支持循环查找；
  - 新增 `clearSelection()` / `getSelection()`；
  - `Ctrl+Shift+F` / `Ctrl+Shift+L` 直接上报 `find` / `clear` 消息，确保 WebView 拥有焦点时快捷键仍生效。
- `SshTerminalView`：
  - `TerminalArea.ContextFlyout` 定义完整 `MenuFlyout`，并由 WebView 消息通过
    `ShowAt(TerminalArea, new FlyoutShowOptions { Position = new Point(x, y) })` 在鼠标位置弹出；
  - `FindBar` 为 XAML 浮层（TextBox + 上一个 / 下一个 / 关闭），通过 `ExecuteScriptAsync` 调用
    `window.term.findNext(...)`；
  - 复制通过 `window.term.getSelection()` 取选区，粘贴复用原有剪贴板逻辑（仍会触发 `UserInput` 进行并发镜像）。
- `MenuFlyoutSubItem` 文本之前不会被 `Loc.Apply` 处理，补充了 `MenuFlyoutSubItem.Text` 的本地化分支。
- 新增 `ConcurrentTerminalView` 的 `Stacked` 布局与 `HorizontalResizeHandle`：分屏标签页支持上下两个终端，
  拖动中间 8px 分隔条调整比例（20%–80%）。
- `MainWindow.OpenSplitTab`：新建「分屏 · {会话名}」标签页，创建两个使用同一配置的终端并并行连接。

### 15.3 验证

- 实际右键终端：菜单正常弹出，复制在无选区时禁用，快捷键提示与中英文文案正确；
- 点击「查找」显示右上角查找栏，输入即调用 xterm 查找；
- 「布局 → 水平分屏」生成上下两个窗格，「垂直分屏」生成左右两个窗格，分隔条可拖动；
- 构建：`dotnet build src/MobaDesk/MobaDesk.csproj -c Debug -p:Platform=x64`，0 警告 0 错误。

---

## 16. 2026-11-20 增补：Ctrl + 鼠标滚轮缩放终端字号

### 16.1 功能

- 在终端区域按住 `Ctrl` 滚动鼠标滚轮即可放大 / 缩小字号，每滚动一格缩放 1px，范围 9–32（与设置中的字号范围一致）。
- 缩放后自动重排（FitAddon），行列数变化会立即通过 `resize` 消息同步给远端 PTY，避免 shell 换行错位。
- 缩放结果会写入 `settings.json` 的 `TerminalFontSize`（600ms 防抖保存），新建终端沿用该字号。
- 不按 `Ctrl` 时滚轮仍然只滚动终端回滚缓冲，不改变字号。

### 16.2 实现

- `terminal.html`：
  - 在 `host` 上以 **捕获阶段** 监听 `wheel`（`{ passive: false, capture: true }`），命中 `ctrlKey` 时
    `preventDefault()` + `stopPropagation()`，阻止 xterm 自己滚动和 WebView2 的页面缩放；
  - 使用 `deltaY` 累加器（阈值 100，每次事件最多 3 步）区分鼠标滚轮与触控板的细碎滚动；
  - 新增 `applyFontSize` / `zoomBy`，修改 `term.options.fontSize` 后调用 `fitNow(true)` 并上报
    `{ type: 'zoom', fontSize }`；同时对外暴露 `window.term.zoom(delta)` / `resetZoom()` / `getFontSize()`。
- `SshTerminalView`：
  - 处理 `zoom` 消息，`ApplyZoom` 将字号限制在 9–32 并写入 `App.Settings.TerminalFontSize`，
    由 `_fontSaveTimer`（`DispatcherQueueTimer`，600ms 单次）触发 `App.Settings.Save()`，避免滚动时频繁写盘；
  - `DisposeAsync` 中停止该定时器。
- WebView2 的 `IsZoomControlEnabled` 原本已设为 `false`，因此不会出现整页缩放与终端字号同时变化的问题。

### 16.3 验证

- 本机 WSL Debian 提供 SSH（密钥认证）测试会话：
  - `Ctrl + 滚轮上` 2 格：14 → 16，日志 `Terminal font size changed to 15/16`，`settings.json` 同步为 16；
  - `Ctrl + 滚轮下` 4 格：16 → 12；
  - 连续放大到上限停止在 32；
  - 不按 `Ctrl` 滚动：字号保持不变；
  - 截图确认终端文字与光标随字号放大，提示符换行正常。
- 构建：`dotnet build src/MobaDesk/MobaDesk.csproj -c Debug -p:Platform=x64`，0 警告 0 错误。

---

## 17. 2026-11-20 修正：文件列表改为双击进入目录

### 17.1 变更

- 文件列表（SFTP / FTP）原来是 `IsItemClickEnabled + ItemClick`，**单击**文件夹就会进入下一级，容易误触。
- 现在：**单击只做选中**（含 Ctrl / Shift 多选），**双击文件夹才进入下一级**，双击文件的编辑行为保持不变。
- 为保留键盘操作，新增 `PreviewKeyDown`：选中条目后按 `Enter` 打开（文件夹进入、文件打开编辑器）。

### 17.2 实现

- `FileBrowserView.xaml`：移除 `IsItemClickEnabled="True"` 与 `ItemClick="OnEntryClick"`，
  改为 `PreviewKeyDown="OnEntriesKeyDown"` + 保留 `DoubleTapped`。
- `FileBrowserView.xaml.cs`：
  - 删除 `OnEntryClick`；
  - `OnEntriesDoubleTapped` 改为 `async`，统一调用新的 `OpenEntryAsync(entry)`；
  - `OpenEntryAsync` 对文件夹调用 `NavigateAsync`，对文件调用 `App.Shell.OpenRemoteFileEditor`；
  - 新增 `OnEntriesKeyDown`（`VirtualKey.Enter` → `OpenEntryAsync`）。
  - 注意：必须用 **`PreviewKeyDown`**，ListViewItem 会先处理 `KeyDown` 中的 Enter，普通 `KeyDown` 收不到。

### 17.3 验证（本机 pyftpdlib 21 端口 → 127.0.0.1:2121，用户 tester）

用 UI 自动化 + UIA 读取路径框值验证：

| 操作 | 路径框结果 |
| --- | --- |
| 点击「上一级」箭头 | `/docs` → `/` |
| 单击 `docs` 文件夹 | 仍为 `/`（仅选中，左侧蓝条高亮） |
| 双击 `docs` 文件夹 | `/docs` |
| 选中 `logs` 后按 Enter | `/logs` |
| 双击 `guide.md` | 新标签页打开编辑器（内容 `# guide`，9 B · UTF-8） |

- 构建：`dotnet build src/MobaDesk/MobaDesk.csproj -c Debug -p:Platform=x64`，0 警告 0 错误。

---

## 18. 2026-11-20 增补：编辑器语法高亮（CodeMirror）

### 18.1 功能

- 远程文件编辑器不再是纯 TextBox，改为 **WebView2 + CodeMirror 5**，按文件类型自动语法高亮。
- 标题栏新增语言徽标（`Shell` / `Python` / `JSON` / `Markdown` / `Properties` / `Plain Text` …）。
- 编辑器带行号、括号匹配、自动闭合括号、当前行高亮、MobaDesk 深色配色（注释灰斜体、关键字紫、字符串绿、数字橙、内建/属性蓝、标签红等）。
- 保留原有能力：`Ctrl+S` 保存（WebView 内由 JS 监听并回调 C#，WebView 外由 XAML 快捷键处理）、只读探测与「只读」锁标记、未保存标记 `*` 与关闭前确认、UTF-8/UTF-16 BOM 检测、CRLF/LF/CR 换行保持、2 MB 与二进制保护。

### 18.2 资源（离线内置）

`src/MobaDesk/Assets/editor/`（`Content Include="Assets\**\*"` 自动复制到输出）：

- `index.html`：编辑器容器、深色主题、与 C# 的消息桥（`ready` / `change` / `save`）。
- `codemirror.min.js` / `codemirror.min.css`：CodeMirror 5.65.16（MIT，`LICENSE.txt` 一并内置）。
- `mode/**.min.js`：clike、shell、python、javascript、xml、css、htmlmixed、markdown、yaml、sql、php、go、rust、ruby、perl、powershell、dockerfile、properties、toml、lua、swift、sass、cmake、nginx、diff、vb、r、protobuf、dart。
- `addon/edit/matchbrackets`、`addon/edit/closebrackets`、`addon/selection/active-line`。

### 18.3 实现要点

- `Models/EditorLanguage.cs`：文件名 → CodeMirror 模式 + 显示名。覆盖 `.bashrc`/`.profile` 等无扩展名脚本、`Dockerfile`、`Makefile`/`CMakeLists.txt`、`nginx.conf`、`.env` 等特例，并处理 `*.bashrc.old` 之类的变体；未知类型回退纯文本。
- `Views/RemoteEditorView`：
  - `EditorWeb`（WebView2）替代 `EditorBox`；`EnsureEditorAsync` 映射虚拟主机 `mobadesk.local` 到 `Assets` 并导航到 `https://mobadesk.local/editor/index.html`，等待 `ready` 消息（15s 超时）。
  - `SetEditorContentAsync` 通过 `ExecuteScriptAsync` 调用 `window.editorHost.setContent(text, mode, readOnly, fontFamily, fontSize)`。
  - `change` 消息 → 250ms 防抖后 `getValue()` 取文本，与 `_loadedText` 比较决定 `*` 标记（撤销回原文也会正确清除）。
  - `save` 消息 → 复用 `SaveAsync`（`getValue()` → `NormalizeLineEndings` → `Encode` → 写回），保存后刷新字节数显示。
  - 只读时下发 `setReadOnly(true)`，CodeMirror 仍可选中/复制但不能输入。
- 新增 `System.Text.Json` 序列化调用复用 `MobaDeskJsonContext.Default.String`，保持 AOT 友好。
- 注意：CodeMirror 会把文档换行统一为 `\n`，因此换行保持依赖 C# 端 `NormalizeLineEndings`（按加载时探测的 `_newLine` 还原），已实测 CRLF / BOM 往返无损。

### 18.4 验证（本机 pyftpdlib FTP 会话 + UI 自动化 + 截图）

| 文件 | 语言徽标 | 结果 |
| --- | --- | --- |
| `.bashrc` | Shell | 注释斜体灰、`case/esac/shopts` 关键字紫、变量/数字/字符串着色 |
| `app.py` | Python | 关键字、字符串、内置函数、docstring 均正确着色 |
| `config.json` | JSON | 键/字符串/数字/布尔分别着色 |
| `README.md` | Markdown | 标题、粗体/斜体、行内代码、引用、链接着色 |
| `crlf.ini` | Properties | 段落与键值着色；保存后文件仍为 34 字节 CRLF |
| `readonly.sh`（只读属性） | Shell | 锁定图标 +「只读」+ 保存禁用，点击后键盘输入无效，磁盘内容未变 |
| `bom.txt`（UTF-8 BOM） | Plain Text | 保存后仍有 `EF BB BF`，大小 18 字节 |
| 编辑保存 | — | 输入后标签出现 `*`，`Ctrl+S` 后 `*` 消失、状态显示「已保存」、磁盘内容更新 |

- 构建：`dotnet build src/MobaDesk/MobaDesk.csproj -c Debug -p:Platform=x64`，0 警告 0 错误。

补充说明：编辑器 WebView2 关闭了默认右键菜单（`AreDefaultContextMenusEnabled = false`），避免用户点到「刷新」导致未保存内容丢失；复制/粘贴仍可用 `Ctrl+C` / `Ctrl+V`。

---

## 19. 2026-11-20 当日开发总结

> 本节汇总 2026-11-20 一天的全部改动（对应第 8–18 节），作为交付与回顾入口。
> 结论：**编译 0 警告 0 错误，全部功能经本机真实服务器 + UI 自动化截图验证通过。**

### 19.1 功能交付

| 模块 | 交付内容 | 关键实现 |
| --- | --- | --- |
| 并发会话 | 多选 SSH 会话在**同一个标签页**内自动排布：每行 2 列，多余会话进入下一行并自动铺满；窗格头部显示会话名/端点/关闭按钮 | `Views/ConcurrentTerminalView`、`Views/ConcurrentPane`、`MainWindow.OpenConcurrentAsync/AddConcurrentTab` |
| 并发输入同步 | 任一并发窗格的键盘输入、粘贴内容、命令行都实时镜像到其他窗格；关闭「参与并发」可单独排除 | `SshTerminalView.UserInput` + `MainWindow.ConcurrentSiblings`（粘贴改为走 `UserInput`，不再直连会话） |
| 分屏 | 终端右键「布局 → 水平/垂直分屏」在新标签页打开上下/左右两个终端，分隔条可拖动（20%–80%） | `ConcurrentTerminalView.TerminalSplitOrientation`、`HorizontalResizeHandle`、`MainWindow.OpenSplitTab` |
| 文件列表 | 新增 **Unix 权限列**（SFTP 属性 / FTP MLSD，缺失时回退 LIST 解析）、**文件类型彩色图标**、**文件与文件夹各自的右键菜单**（打开/编辑/下载/上传到此处/新建文件夹/复制路径/重命名/刷新/删除） | `RemoteEntry.UnixMode/PermissionsText/IconBrush`、`FileIconPalette`、`FileBrowserView` 上下文菜单 |
| 文件打开方式 | **单击仅选中**，**双击文件夹进入下一级**，**双击文件在新标签页打开编辑器**，选中后按 `Enter` 同样可打开 | 移除 `IsItemClickEnabled/ItemClick`，新增 `OpenEntryAsync` 与 `PreviewKeyDown` |
| 远程编辑器 | 双击文件打开编辑标签页；无写权限时左下角显示锁形「只读」并禁用保存；`Ctrl+S` 保存、未保存 `*` 标记与关闭前确认；UTF-8/UTF-16 BOM 与 CRLF/LF/CR 换行无损保持 | `Views/RemoteEditorView`、`IRemoteFileSystem.ReadFileAsync/WriteFileAsync/CanWriteAsync` |
| 编辑器语法高亮 | 内嵌 CodeMirror 5（离线资产），按文件类型自动着色，标题栏显示语言徽标（Shell / Python / JSON / Markdown / Properties / Plain Text…），带行号、括号匹配、深色配色 | `Assets/editor/*`、`Models/EditorLanguage.cs`、`RemoteEditorView` WebView2 消息桥 |
| 终端右键菜单 | 复制 / 粘贴 / 查找 / 清屏 / 重新连接 / 选中文本（全选、取消选择）/ 布局（水平、垂直分屏）/ 更多（SFTP、监控面板）；无选区时「复制」禁用 | `TerminalMenu`、`terminal.html` 的 `contextmenu`/`selection` 消息 |
| 终端查找 | 右上角查找栏，基于 xterm 缓冲区扫描，支持上一个/下一个与循环查找（`Ctrl+Shift+F`） | `terminal.html findNext`、`SshTerminalView` 查找栏 |
| 终端字号缩放 | **Ctrl + 鼠标滚轮**放大/缩小字号（9–32），自动重排并向 PTY 同步行列，字号写入设置供新终端沿用 | `terminal.html` 捕获阶段 wheel 监听 + `fitNow` 重排；`SshTerminalView.ApplyZoom` 限制 9–32 并防抖写盘 |
| 交互修复 | 「参与并发」文字颜色被 CheckBox 主题色覆盖（改为 Content 内固定色 TextBlock）；终端与 SFTP 侧栏、并发窗格之间的**分隔条现在都可拖动** | `SshTerminalView.SidePanelSplitter`、`VerticalResizeHandle` |

### 19.2 关键问题与修复

| 问题 | 原因 | 修复 |
| --- | --- | --- |
| 并发粘贴不同步 | 粘贴直接写 `_session`，绕过了 `UserInput` | 改为写字节并触发 `UserInput`，由主窗口统一镜像 |
| 未勾选广播时输入不同步 | 镜像逻辑只在广播开启时执行 | 同一并发标签页内的窗格始终互为镜像，广播开关改为「排除/参与」语义 |
| 分隔条拖不动 | 完全透明的 `Border` 在部分区域不接收指针；`Border` 在 WinUI 中为 sealed | 新增 `VerticalResizeHandle`/`HorizontalResizeHandle`（继承 `Grid`），底色 alpha 16 + 内线 `IsHitTestVisible=False` |
| 子菜单标题空白 | `Loc.ApplyTree` 只处理 `MenuFlyoutItem` | `Loc` 增加 `MenuFlyoutSubItem` 分支 |
| 文件列表单击即进目录（误触） | `IsItemClickEnabled` + `ItemClick` | 改为双击 + `PreviewKeyDown`（`KeyDown` 会被 ListViewItem 先吃掉） |
| 编辑器换行/编码被改坏 | WinUI TextBox 会把换行统一为 `\r`；CodeMirror 会把文档统一为 `\n` | 加载时探测 `_newLine` 与 BOM，保存前 `NormalizeLineEndings` + `Encode` 还原（实测 CRLF 34 B、BOM `EF BB BF` 往返无损） |
| 编辑器右键「刷新」可能丢内容 | WebView2 默认上下文菜单 | `AreDefaultContextMenusEnabled = false` |
| 缩放触发时写盘抖动 | 每次滚轮都 `Settings.Save()` | 600ms 防抖 `DispatcherQueueTimer` |

### 19.3 技术决策

- **终端与编辑器都走 WebView2 + 离线前端资产**（xterm.js / CodeMirror 5，`Assets/**` 通配复制），C# 只做连接、IO、编码与消息桥，避免自研渲染与高亮；CodeMirror 为 MIT，`LICENSE.txt` 一并内置。
- **虚拟主机映射** `mobadesk.local → Assets`，页面用 `https://mobadesk.local/...` 加载，避免 `file://` 限制。
- **C# ↔ JS 消息**统一 JSON（`postMessage` + `TryGetWebMessageAsString`），字符串统一用源生成的 `MobaDeskJsonContext.Default.String` 序列化，保持 AOT 友好。
- **拖拽分隔条**统一封装为两个 `Grid` 派生控件，使用指针捕获与 `SizeWestEast`/`SizeNorthSouth` 光标，暴露 `DragMoved`（父容器内相对坐标）。
- **本地化**继续走 key + 附加属性，新增 `Terminal.*`、`Tab.*`、`Editor.*`、`File.*` 等键，并补齐子菜单支持。

### 19.4 新增与修改文件

新增：

- `Views/ConcurrentTerminalView.xaml(.cs)`、`Views/ConcurrentPane.xaml(.cs)`
- `Views/RemoteEditorView.xaml(.cs)`
- `Controls/VerticalResizeHandle.cs`、`Controls/HorizontalResizeHandle.cs`
- `Models/FileIconPalette.cs`、`Models/EditorLanguage.cs`
- `Assets/editor/`：`index.html`、`codemirror.min.js(.css)`、`mode/**`（30+ 语言）、`addon/edit|selection/**`、`LICENSE.txt`

修改：

- `MainWindow.xaml.cs`（并发标签页、分屏、编辑器打开、关闭确认、会话统计）
- `Views/SshTerminalView.xaml(.cs)`（右键菜单、查找栏、侧栏分隔条、广播颜色、缩放、粘贴镜像）
- `Views/FileBrowserView.xaml(.cs)`（权限列、彩色图标、右键菜单、双击/Enter 打开）
- `Models/RemoteEntry.cs`、`Services/FileSystems/{IRemoteFileSystem,SftpRemoteFileSystem,FtpRemoteFileSystem}.cs`（读写、可写探测、权限解析）
- `Assets/terminal.html`（右键/选区/查找/清屏消息、Ctrl+滚轮缩放、`findNext`）
- `Services/Localization.cs`、`Localization/Loc.cs`、`README.md`、`docs/PROGRESS.md`

### 19.5 验证记录

- **构建**：`dotnet build src/MobaDesk/MobaDesk.csproj -c Debug -p:Platform=x64` → **0 警告 0 错误**（本轮共验证 10+ 次，含每次改动后的增量构建）。
- **测试环境**：本机 `pyftpdlib`（127.0.0.1:2121/2122，`tester` 可写、`viewer` 只读）+ WSL Debian SSH（密钥认证）+ WSL Debian SSH（Ed25519 密钥认证）；应用以 `MOBADESK_DATA_DIR` 指向临时数据目录运行，不影响真实配置。
- **验证手段**：Win32 P/Invoke + UI Automation 点击/截图，UIA 读取路径框值作为断言；WebView 内（xterm/CodeMirror）通过日志与截图核对。
- **覆盖场景**：并发排布（3 会话 = 2+1 铺满）、输入/粘贴镜像、权限列与两类右键菜单、彩色图标（12 种类型）、双击进入目录与双击编辑、只读编辑器、CRLF/BOM 往返、语法高亮（Shell/Python/JSON/Markdown/Properties/Plain Text）、终端右键菜单与查找、Ctrl+滚轮缩放（14→16→12、上限 32、普通滚轮不缩放）、分隔条拖动。
- **清理**：临时数据目录、测试服务器、验证输出目录（`bin\verify*`）、WSL 测试密钥与自动化脚本均已删除；代码中无遗留自动化开关（`MOBADESK_AUTOTEST*` 已移除）。

### 19.6 已知限制与后续可做

| 项 | 说明 |
| --- | --- |
| 编辑器搜索/替换 | CodeMirror 未内置 `search` 插件，目前依赖 `Ctrl+F` 的浏览器行为（已关闭浏览器快捷键，暂不可用） |
| 编辑器字号/主题 | 固定 13px 与内置深色配色，未接入设置项 |
| 并发窗格缩放 | `Ctrl+滚轮` 只改变当前窗格字号，其他窗格保持各自大小 |
| 分屏/并发布局持久化 | 布局（方向、比例）不写入配置，重启后恢复为默认 |
| CodeMirror 版本 | 5.x 已进入维护模式，后续如需更好体验可评估迁移到 CodeMirror 6（需打包 ESM） |
| 编辑器右键菜单 | 为防误点刷新已禁用，复制/粘贴走快捷键 |

### 19.7 交付提示

- 若应用正在运行，需要**关闭后重新启动**，才能加载新的 `Assets`（编辑器页面、终端页面）与最新程序集。
- 相关文档：本文件第 9–18 节为逐项细节，`README.md` 的功能列表与使用步骤已同步更新。

---

## 20. 2026-11-23 增补：并发命令栏移到底部、多行输入与 ⌃C

### 20.1 需求

- 并发会话窗口顶部的命令栏移到窗口最底部（状态栏正上方）。
- 命令输入框改为多行文本。
- 新增**扫帚图标**按钮用于清空命令。
- 新增**橡皮擦图标**按钮，一键清除所有参与并发会话的终端屏幕。
- 新增 `⌃C` 按钮，向所有参与并发的会话发送 `Ctrl+C`（`0x03`）以中断当前命令。

### 20.2 实现

- `MainWindow.xaml`：
  - 内容区 `Grid` 行定义由 `Auto / *` 改为 `* / Auto`；`TabView` 与空状态放在第 0 行（拉伸），
    `BroadcastBar` 放到第 1 行，位于标签页内容下方、状态栏上方，上边框改 `0,1,0,0`。
  - `BroadcastCommandBox`：`AcceptsReturn="True"`、`TextWrapping="Wrap"`、`MinHeight=56`、`MaxHeight=160`，
    超出高度后自动出现竖向滚动条；命令栏整体随内容自适应高度。
  - 清除命令按钮使用扫帚图标 `EA99`；旁边新增清除终端按钮，使用橡皮擦图标 `E75C`；
    `⌃C` 按钮以等宽字体直接显示 `⌃C`，点击发送中断字符。
- `MainWindow.xaml.cs`：
  - `OnBroadcastCommandKeyDown`：`Enter` 插入换行，`Ctrl+Enter`（`InputKeyboardSource.GetKeyStateForCurrentThread`）发送。
  - `OnBroadcastClearClick` 清空输入并重新聚焦；`OnBroadcastCtrlCClick` 向所有勾选「参与并发」的终端写 `0x03`，
    无成员时提示错误，成功后在状态栏显示发送数量。
  - `OnBroadcastClearTerminalsClick` 对所有参与会话调用 `SshTerminalView.ClearTerminal()`，
    清屏后把焦点还给命令输入框，并在状态栏显示已清除的终端数量。
  - `UpdateBroadcastState` 同步启用/禁用发送、清除终端与 `⌃C` 按钮。
- `SshTerminalView`：新增公开方法 `ClearTerminal()`（执行 `window.term.clear()`，不抢占焦点）；
  原有的终端右键「清屏」复用它并保留聚焦行为。
- `SshTerminalView.SendCommandLine`：多行输入按行下发，把 `\r\n` / `\n` 统一转换为 `\r`（远端 PTY 的 Enter），
  而不是发送裸 `LF`；末尾多余的 `\r` 被裁剪后再追加一个 Enter。
- 本地化（`zh-CN.json` / `en-US.json`）：更新 `Broadcast.Placeholder` 说明「Ctrl+Enter 发送、Enter 换行」，
  新增 `Broadcast.Clear`、`Broadcast.ClearTerminalsToolTip`、`Broadcast.TerminalsCleared`、
  `Broadcast.CtrlCToolTip`、`Broadcast.CtrlCSent`。

### 20.3 验证

- `dotnet build src/MobaDesk/MobaDesk.csproj -c Debug -p:Platform=x64`：0 警告 0 错误。
- 打开 SSH 标签页后点击标题栏「并发输入」：命令栏出现在窗口底部（状态栏上方），命令框为多行，
  右侧可见扫帚、橡皮擦、`⌃C` 与「发送」按钮；窗口截图确认布局与本地化文案正确。
- 在并发会话（k8s-master / k8s-node1 两个窗格）中点击橡皮擦按钮：两个终端屏幕同时清空，
  状态栏显示「已清除 2 个终端屏幕」，焦点保持在命令输入框。
- 注意：如果更新前应用仍在运行，必须**完全退出并重新启动**，否则 `bin` 下的旧程序集会继续被加载，
  会表现为命令栏不可见（改动只存在于源码与新的构建产物中）。

### 20.4 追加：并发标签页默认显示命令栏、修复 Ctrl+Enter

| 问题 / 需求 | 处理 |
| --- | --- |
| 并发会话标签页必须开启「并发输入」才显示底部命令栏 | `TabView` 新增 `SelectionChanged`；`UpdateBroadcastBarVisibility()` 在「全局并发输入开启」**或**「当前选中标签页是 `ConcurrentTerminalView`」时显示命令栏。打开并发 / 分屏标签页后命令栏即默认可见，无需先点开关 |
| 命令栏的作用范围 | 新增 `BroadcastTargets()`：全局开关关闭时只针对当前并发标签页内的窗格；开关开启时扩大到所有标签页中参与并发的会话。发送、清屏、`⌃C` 与参与计数统一使用该目标集合 |
| `Ctrl+Enter` 不发送、命令留在输入框 | 多行 `TextBox` 会在类处理中消费 `Enter`，普通 `KeyDown` / XAML 事件拿不到该按键；改为在构造函数里 `BroadcastCommandBox.AddHandler(UIElement.KeyDownEvent, ..., handledEventsToo: true)`，并同时检查 `Control` / `LeftControl` / `RightControl` 的按下状态。`Enter` 仍插入换行，`Ctrl+Enter` 发送并清空输入框 |
| 验证 | 三个 SSH 会话组成并发标签页，全局开关保持**关闭**：底部命令栏默认显示；输入 `echo CT_ENTER_OK` 按 `Ctrl+Enter` 后输入框清空、三个终端都执行并输出 `CT_ENTER_OK`，状态栏显示「已向 3 个会话发送命令」 |




---

## 21. 2026-11-23 增补：安装程序、自动更新与版本检测

### 21.1 安装程序

| 项目 | 说明 |
| --- | --- |
| 打包工具 | Inno Setup 6（本机已安装到 `C:\Program Files (x86)\Inno Setup 6`）；构建脚本自动在 PATH、Program Files、Program Files (x86)、`%LOCALAPPDATA%\Programs\Inno Setup 6` 和临时目录中查找 `ISCC.exe` |
| 脚本 | `installer\MobaDesk.iss`（安装定义）、`installer\languages\ChineseSimplified.isl`（官方中文语言文件，UTF-8 BOM）、`installer\build-installer.ps1`（先 `publish.ps1`，再编译并生成清单） |
| 安装方式 | 默认按当前用户安装到 `%LOCALAPPDATA%\Programs\MobaDesk`（`PrivilegesRequired=lowest`，可在向导中切换为所有用户）；同一 `AppId` 的后续安装原地升级 |
| 集成 | 开始菜单快捷方式、可选桌面快捷方式、卸载入口；`CloseApplications=yes`，静默升级时自动关闭正在运行的 MobaDesk |
| 多语言 | 安装向导提供 English / 简体中文 |
| 产物 | `installer\output\MobaDesk-Setup-1.0.1.exe`（44.6 MB，自包含精简负载）与 `installer\output\version.json` |
| 忽略 | `.gitignore` 增加 `installer/output/` |

构建命令：

```powershell
.\installer\build-installer.ps1 -BaseUrl https://downloads.example.com/mobadesk
```

`-SkipPublish` 可复用现有 `publish\win-x64`；`-Version` 可覆盖 csproj 中的版本号。

### 21.2 自动更新与版本检测

- 新增 `Services\UpdateService.cs`、`Models\UpdateManifest.cs`：
  - 更新源优先级：环境变量 `MOBADESK_UPDATE_FEED` → 设置 `UpdateFeedUrl` → 程序目录 `updates\version.json`。
  - 支持 `http(s)://`、本地文件路径、目录（自动补 `version.json`）与 `file://`；相对 `url` 按清单所在位置解析。
  - 版本比较使用 `System.Version`（忽略 `-prerelease` / `+build` 后缀），只有清单版本高于当前版本才提示。
  - 下载到 `%TEMP%\MobaDeskUpdate`，实时进度；用 SHA-256 校验安装包，校验失败立即删除并拒绝安装；清单缺少有效 SHA-256 时同样拒绝。
  - 通过 `Process.Start` 运行安装包：`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS`。
- `AppSettings` 新增 `UpdateFeedUrl`、`CheckUpdatesOnStartup`（默认 `true`）、`LastUpdateCheckUtc`；设置界面「关于」页新增更新卡片：
  - 更新源输入框、启动时自动检查复选框、「检查更新」按钮；
  - 检查到新版本后显示「下载并安装」按钮，关闭设置后由主窗口下载并安装；
  - 手动检查时显示「正在检查 / 已是最新 / 发现新版本 / 失败原因」。
- 启动检查：主窗口加载 6 秒后在后台检查；成功检查后写入 `LastUpdateCheckUtc`，24 小时内不重复检查；发现新版本时弹出「立即安装 / 稍后」对话框。若此时已有其它对话框打开（如设置），本次提示跳过并记录日志，避免阻塞启动。
- 版本号来源改为程序集 `AssemblyInformationalVersion`（去除 `+commit` 后缀），状态栏与「关于」页不再硬编码 `v1.0.0`。
- 新增本地化词条 `Update.*`（中英双语，共 20 条）。

### 21.3 验证

- 主工程与 ServiceProbe 构建：0 警告 0 错误。
- 安装包静默安装到 `%TEMP%\MobaDeskInstallTest`：退出码 0，`MobaDesk.exe`、`Assets`、`Resources\Lang` 等文件齐全；安装后的程序可正常启动；`unins000.exe /VERYSILENT` 退出码 0 且安装目录被完全删除。
- 更新清单探针（本地文件源 + 相对下载地址）：
  ```
  == update ...\MobaDeskFeed\version.json ==
     current=1.0.0
     latest=1.0.1 newer=True
     installer=...\MobaDeskFeed\MobaDesk-Setup-1.0.0.exe
     downloaded=...\MobaDeskUpdate\MobaDesk-Setup-1.0.0.exe size=46760887
  PASS: update manifest read, compared and verified.
  ```
- 应用内启动检查（`MOBADESK_UPDATE_FEED` 指向 1.0.1 清单）：日志确认比较结果为新版本；若用户恰好打开了设置对话框，则提示因「同一时间只能有一个 ContentDialog」而跳过并记录 WARN，不影响应用运行。
- 安装包 SHA-256 写入 `version.json`，探针下载后校验通过。

### 21.4 已知限制

- 自动更新依赖可访问的 `version.json` 与安装包地址；没有托管服务器时可使用程序目录下的 `updates\` 离线更新源。
- 安装包未做代码签名，Windows SmartScreen 可能提示未知发布者。
- 默认按用户安装；从 `publish\win-x64` 直接运行的便携副本不会自动更新，安装程序会另外安装一份到用户程序目录。
- 更新包为完整安装包，暂不支持增量/差分包；下载与安装期间需要保持网络与磁盘可用。
- 若更新时存在其它模态对话框，启动检查的提示会跳过（仍可在「设置 → 关于」手动检查）。

---

## 22. 2026-11-23 调整：「参与并发」复选框移至顶部工具栏

- 原位置：终端左下角悬浮的深色圆角条（会遮挡部分终端输出）。
- 新位置：终端顶部工具栏右侧、`SFTP` 与 `监控` 按钮**左侧**，与其它工具栏按钮同一行。
- 实现：`Views\SshTerminalView.xaml` 中把 `BroadcastCheck` 从 `TerminalArea` 的左下角 `Border` 移入右侧按钮 `StackPanel` 的第一项；
  - 文字使用 `CheckBox.Content` 内的 `TextBlock` + `{ThemeResource TextFillColorPrimaryBrush}`，避免选中时被 `CheckBoxForegroundChecked` 染成青色，同时适配深/浅主题；
  - 按钮间距由 2 调整为 4，复选框右侧额外留 4px；
  - 事件、`IsBroadcastMember` 与并发同步逻辑保持不变。
- 验证：并发标签页两个窗格均在顶部显示「参与并发」复选框（默认勾选），位于 SFTP / 监控左侧；点击可正常切换，底部命令栏计数随之变化；构建 0 警告 0 错误。

---

## 23. 2026-11-23 调整：并发命令栏高度可拖动

- 在终端内容与底部命令栏之间新增 `Controls\HorizontalResizeHandle`（`BroadcastSplitter`，8px 高、1px 中线、上下拖动光标）；
  - 内容区 `Grid` 行定义改为 `* / Auto / Auto`：第 0 行终端、第 1 行拖动条、第 2 行命令栏；
  - 命令输入框移除固定 `MaxHeight`，随命令栏高度拉伸，内部自动出现滚动条；
  - 拖动条可见性与命令栏绑定：命令栏显示时同时显示拖动条，隐藏时一并收起。
- `MainWindow.OnBroadcastSplitterDragMoved` 根据指针位置计算命令栏高度：
  - 最小 72（内边距 + 一行输入），最大为内容高度减去终端最小 220；
  - 拖动时终端与命令栏实时重新分配空间，输入框、工具按钮随之重排。
- 注意：`DragMoved` 是普通 CLR 事件，XAML 属性绑定不会真正挂接，必须在构造函数中 `BroadcastSplitter.DragMoved += OnBroadcastSplitterDragMoved`（与 `ConcurrentTerminalView` / `SshTerminalView` 中其它拖动条一致）。
- 验证：在真实 SSH 标签页中开启并发输入，拖动命令栏上方横线向上移动 150px，命令栏高度由 72 增至约 226，终端相应缩短；反向拖动可缩小，且不会超过最小/最大限制；构建 0 警告 0 错误。
