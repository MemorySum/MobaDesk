# MobaDesk

MobaDesk 是一个使用 **WinUI 3** 构建的 Windows 多协议服务器工作台，定位类似 MobaXterm，但 **不限制并发 SSH 连接数**。

> 开发进度与实现细节见 [docs/PROGRESS.md](docs/PROGRESS.md)。

## 功能

- **SSH 终端**：基于 xterm.js（WebView2）的完整终端仿真，支持 ANSI 颜色、回滚、窗口自适应、Ctrl+Shift+C/V 复制粘贴、**Ctrl + 鼠标滚轮缩放字号**、密码 / 私钥 / 键盘交互式认证；终端内右键可复制、粘贴、查找、清屏、重新连接、选中文本、选择布局（新建水平 / 垂直分屏）以及打开 SFTP / 监控面板。
- **SFTP 文件传输**：每个 SSH 标签页内置 SFTP 面板，支持目录浏览、上传、下载（含递归下载目录）、新建目录、重命名和删除；**拖动面板左边缘可调整 SFTP / 监控侧栏宽度**；文件列表显示 Unix 权限（如 `drwxr-xr-x`），图标按文件类型自动着色（文件夹、代码、配置、文档、压缩包、图片、音视频、二进制等），文件和文件夹均有右键菜单（打开、编辑、下载、上传到此处、在此新建文件夹、复制路径、重命名、刷新、删除）。**单击仅选中，双击文件夹进入下一级目录、双击文件在新标签页中打开文本编辑器**（也可选中后按 Enter）；编辑器内嵌 CodeMirror，**按文件类型自动语法高亮**（Shell / Python / JavaScript / TypeScript / JSON / HTML / XML / CSS / SCSS / Markdown / YAML / SQL / PHP / Go / Rust / Ruby / Perl / PowerShell / Dockerfile / Properties / TOML / C / C++ / C# / Java / Swift / Lua / Dart / CMake / Nginx / Diff 等，其他类型按纯文本显示），带行号与括号匹配；当前账号没有写权限时，编辑器左下角显示锁形「只读」标记，保存按钮禁用。
- **FTP / FTPS**：独立的 FTP 会话标签页，支持显式 TLS、目录管理与文件传输。
- **RDP 远程桌面**：生成 `.rdp` 配置并调用 Windows 原生 `mstsc`，支持 NLA、分辨率、全屏、剪贴板与磁盘重定向。
- **服务器监控**：SSH 标签页内置监控面板，实时显示 CPU、内存、Swap、负载、网络吞吐、磁盘、运行时间与高 CPU 进程（支持 Linux `/proc`）。
- **无限标签页**：并发 SSH / SFTP / FTP / RDP 会话数量不受限制。
- **凭据安全**：密码与私钥口令使用 Windows DPAPI（当前用户）加密存储，不落明文；RDP 密码同样以 DPAPI 密文写入 `.rdp` 文件。
- **会话管理**：搜索、编辑、复制、删除；支持文件夹分组（新建 / 重命名 / 删除 / 折叠），可把会话**拖拽**到任意文件夹或根目录，分组信息持久化在 `folders.json`。
- **并发会话**：Ctrl / Shift 多选多个 SSH 会话后双击，会在同一个「并发会话」标签页里自动排布：每行两列，多余的会话进入下一行，最后一行只剩一个时自动铺满整行，**拖动窗格之间的分隔条可调整左右宽度**；同一标签页内勾选「参与并发」的窗格会**实时互相同步**键入和粘贴的内容；**并发会话标签页底部默认显示多行命令栏**（**拖动命令栏上方的横线可调整输入区高度**），可向当前标签页中参与并发的会话统一下发命令（**Ctrl+Enter 发送、Enter 换行**，扫帚图标清空命令，橡皮擦图标一键清除参与会话的终端屏幕，⌃C 按钮中断参与会话）；开启标题栏的「并发输入」后，键入镜像与命令下发范围扩展到其它标签页中所有参与并发的 SSH 会话；每个终端顶部工具栏中（SFTP / 监控按钮左侧）的「参与并发」复选框可临时把某个会话排除在外。
- **安装与自动更新**：提供 Inno Setup 安装包（单个 `.exe`，按当前用户安装，创建开始菜单 / 可选桌面快捷方式与卸载入口）；内置更新检测：从 `version.json` 更新源比对版本、下载安装包、校验 SHA-256 后静默原地升级；启动时自动检查（24 小时一次）或从「设置 → 关于」手动检查。
- **窗口材质**：Mica / Mica Alt / Acrylic / 纯色 四种材质，可随时切换；每种材质带独立的主题化薄纱层，即使在深色或纯色壁纸下也能一眼看出区别。
- **界面模式**：**优雅动画**（启动渐显、侧栏滑入/滑出、列表重排过渡）与 **简洁模式**（关闭全部动画，界面即时响应）两种，选择会写入设置并在下次启动时沿用。
- **主题**：跟随系统 / 深色 / 浅色。
- **多语言**：简体中文 / English；词条以 JSON 存放在 `Resources/Lang/zh-CN.json` 与 `en-US.json`，可直接编辑，无需改代码。
- **设置界面**：左侧分类导航（外观 / 终端 / 连接与监控 / 常规 / 关于），点击分类在右侧打开对应设置页，所有修改即时预览；对话框居中显示、圆角卡片，按住标题栏任意位置即可像主窗口一样拖动。
- **即时生效**：主题、材质、语言以及终端字号等在设置中修改后**立即应用**，无需重启；保存后也会同步到已打开的终端与监控面板；未保存关闭设置会自动还原。
- **安全与可靠性**：下载时拒绝包含路径分隔符或 `..` 的远端文件名，防止写出目标目录；SFTP 保存先写临时文件再原子替换（写入符号链接时保留链接，并尽量保留文件权限），下载先写 `.part` 再改名；关闭主窗口前会逐个提示保存未保存的编辑器内容。

## 环境要求

- Windows 10 1809+ / Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/) 或更高版本
- Microsoft Edge WebView2 Runtime（Windows 11 自带；Windows 10 通常随 Edge 安装）

## 构建与运行

```powershell
dotnet build .\src\MobaDesk\MobaDesk.csproj -c Debug
.\src\MobaDesk\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\MobaDesk.exe
```

> 项目使用 `WindowsAppSDKSelfContained=true`，无需单独安装 Windows App Runtime。

### 发布

推荐使用脚本，会打印最终体积：

```powershell
.\publish.ps1                      # 自包含精简版 -> publish\win-x64
.\publish.ps1 -Fx                  # 依赖运行时版 -> publish\win-x64-fx（体积最小）
.\publish.ps1 -R2R                 # 追加 ReadyToRun 预编译（启动更快，体积略大）
.\publish.ps1 -Aot                 # Native AOT（实验性，见下文）
```

等价的原始命令：

```powershell
dotnet publish .\src\MobaDesk\MobaDesk.csproj -c Release -r win-x64 --self-contained true -o .\publish\win-x64
```

| 发布模式 | 体积 | 文件数 | 目标机要求 |
| --- | --- | --- | --- |
| 自包含精简（默认） | **约 166 MB** | 304 | 无，复制即用 |
| 依赖运行时（`-Fx`） | **约 43 MB** | 49 | .NET 8 Desktop Runtime + Windows App Runtime 1.8 |
| 自包含 + ReadyToRun（`-R2R`） | 约 220 MB | 304 | 无，启动更快 |
| Native AOT（`-Aot`） | 66.7 MB | 87 | 需 MSVC 链接器；**WinUI 下无法运行**（见下） |

**精简了什么**：Windows App SDK 1.8 会附带整套 Windows AI 运行时（ONNX Runtime、DirectML、
Windows.AI / Workloads / Widgets 投影）和 86 个语言资源目录，MobaDesk 完全用不到。发布后自动
删除这些文件与 `*.pdb`，可省约 50 MB；`-p:MobaDeskSlimPayload=false` 可关闭该裁剪。

**关于 Native AOT**：`-Aot` 会启用 ILC 编译。代码已按 AOT 要求改造（JSON 全部使用源生成序列化 +
`JsonTypeInfo` 重载，WinRT 相关类型标记为 partial），AOT 分析器告警为 **0**，ILC 也能编译并链接成功
（产物约 67 MB）。但 **Windows App SDK / WinUI 3 目前不支持 Native AOT**，实测启动即崩溃：

```
System.ArgumentException: Arg_ArgumentException
   at ABI.Microsoft.UI.Xaml.Controls.IItemsControlMethods.set_ItemsSource(...)
   at MobaDesk.MainWindow..ctor()
```

原因是 CsWinRT 把托管集合（`ObservableCollection<T>`）封送给 `ItemsControl.ItemsSource` 时需要
运行时代码生成，AOT 下不可用。因此该模式仅作留档，日常发布请使用默认模式或 `-Fx`。

前置条件与用法：

```powershell
# 安装链接器（随后在安装器中勾选「使用 C++ 的桌面开发」）
winget install --id Microsoft.VisualStudio.2022.BuildTools -e
.\publish.ps1 -Aot              # 预检链接器 -> 缺失时给出提示
.\publish.ps1 -Aot -Force       # 跳过预检，直接尝试 ILC + 链接
```

脚本会自动把 `…\Microsoft Visual Studio\Installer` 加入 `PATH`，否则 ILC 调用 `vswhere.exe`
失败会导致链接命令拼错。

**关于裁剪（Trimming）**：`PublishTrimmed` 会在 WinUI 启动时崩溃（CsWinRT 封送代码被裁掉），
已实测确认不可用，请勿开启。

### 安装程序与自动更新

构建安装包（需要 [Inno Setup 6](https://jrsoftware.org/isdl.php)，可用 `winget install --id JRSoftware.InnoSetup -e` 安装）：

```powershell
.\installer\build-installer.ps1 -BaseUrl https://downloads.example.com/mobadesk
```

- 默认先运行 `publish.ps1`（自包含精简版），再用 `installer\MobaDesk.iss` 编译，输出：
  - `installer\output\MobaDesk-Setup-<version>.exe`（约 45 MB）
  - `installer\output\version.json`（含版本、下载地址、SHA-256、体积）
- 安装程序按当前用户安装到 `%LOCALAPPDATA%\Programs\MobaDesk`，无需管理员权限；同一 `AppId` 的后续安装会原地升级并保留会话与设置。卸载入口位于「设置 → 应用」或开始菜单。
- 将 Setup 与 `version.json` 上传到同一目录，在「设置 → 关于 → 更新源」填写 `version.json` 的 URL；也可设置环境变量 `MOBADESK_UPDATE_FEED`，或把两者放到程序目录的 `updates\` 下作为离线更新源。
- 检查到新版本后，MobaDesk 下载安装包到 `%TEMP%\MobaDeskUpdate`，校验 SHA-256 后以 `/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS` 静默运行安装程序并退出，实现自动升级。
- 未做代码签名的安装包在首次运行时可能触发 SmartScreen 提示；正式分发建议使用代码签名证书。

`version.json` 示例：

```json
{
  "version": "1.0.1",
  "url": "MobaDesk-Setup-1.0.1.exe",
  "sha256": "B1946AC92492D2347C6235B4D2611184...",
  "size": 46760887,
  "notes": "修复若干问题",
  "publishedAt": "2026-11-23T12:00:00Z"
}
```

`url` 可以是绝对地址，也可以相对于 `version.json` 所在目录。缺少有效 SHA-256 的更新会被拒绝安装。

### 便携模式 / 多实例

默认数据目录为 `%LOCALAPPDATA%\MobaDesk`。设置环境变量 `MOBADESK_DATA_DIR` 可切换到隔离的数据目录（例如放在 U 盘）：

```powershell
$env:MOBADESK_DATA_DIR = "D:\MobaDeskData"
.\MobaDesk.exe
```

## 使用说明

1. 点击左侧 **新建**，选择 SSH / SFTP / FTP / RDP；**新建 → 新建文件夹** 可以创建会话分组。
2. **Ctrl / Shift 多选**多个 SSH 会话，双击其中任意一个即可在同一个「并发会话」标签页中打开：窗口自动按每行两列排布，多余的会话进入下一行并自动铺满；右键菜单里也有「打开所选会话」。
3. 拖动左侧会话来归类：拖到文件夹行放入该文件夹，拖到列表空白处或侧栏头部回到根目录；右键会话也可用 **移动到** 菜单。
4. **并发输入**：同一个「并发会话」标签页内，在任意勾选「参与并发」的终端里键入或粘贴（`Ctrl+Shift+V`），内容会**实时镜像**到同标签页的其它参与窗格；该标签页底部**默认显示多行命令栏**，**拖动命令栏上方的横线可以调整输入区高度**（终端至少保留 220px，命令框最小 72px）；可把一条或多条命令同时下发到当前标签页中参与并发的 SSH 会话（`Ctrl+Enter` 发送、`Enter` 换行）；点击标题栏的「并发输入」按钮后，键入镜像与命令下发范围扩展到其它标签页中的参与会话；命令栏右侧的扫帚按钮清空输入，橡皮擦按钮清除参与会话的终端屏幕，`⌃C` 按钮向参与会话发送 `Ctrl+C` 中断当前命令。
5. 每个终端顶部工具栏、SFTP / 监控按钮左侧有「参与并发」复选框，取消勾选即可把该会话临时排除在并发输入/镜像之外（再次勾选恢复）。
6. 文件夹行可点击左侧箭头折叠/展开，右键文件夹可重命名或删除（删除文件夹不会删除会话，只把会话移回根目录）。
7. SSH 标签页顶部可以打开 **SFTP** 或 **监控** 侧边面板，拖动面板左边缘可调整宽度；文件列表显示名称、**权限**、大小和修改时间，右键文件/文件夹可使用对应操作菜单；**单击只选中**，**双击文件夹进入下一级目录**、**双击文件**会在新标签页打开文本编辑器（选中后按 Enter 效果相同）。编辑器按文件扩展名自动选择语法高亮（标题栏显示语言，如 `Bash`、`Python`、`JSON`），`Ctrl+S` 保存；没有写权限时编辑器左下角显示「只读」锁图标。
8. 终端内使用 `Ctrl+Shift+C` / `Ctrl+Shift+V` 复制粘贴，`Ctrl+Shift+F` 查找，`Ctrl+Shift+L` 清屏，`Ctrl + 鼠标滚轮` 放大 / 缩小字号（9–32，自动保存为新终端默认字号）；在终端中右键可打开完整操作菜单（复制、粘贴、查找、清屏、重新连接、选中文本、布局分屏、打开 SFTP / 监控）；`Ctrl+T` 新建 SSH，`Ctrl+W` 关闭当前标签页。
9. **设置**（齿轮图标）中可以切换主题、窗口材质与界面语言，改动立即套用到主窗口与所有已打开的标签页。
10. **更新**：「设置 → 关于」可填写更新源、开启或关闭启动时自动检查，并点击「检查更新」；发现新版本后可一键下载、校验并静默安装（见上文「安装程序与自动更新」）。

### 数据位置

| 内容 | 路径 |
| --- | --- |
| 会话配置 | `%LOCALAPPDATA%\MobaDesk\profiles.json` |
| 会话文件夹 | `%LOCALAPPDATA%\MobaDesk\folders.json` |
| 应用设置 | `%LOCALAPPDATA%\MobaDesk\settings.json` |
| 日志 | `%LOCALAPPDATA%\MobaDesk\logs\app.log` |
| RDP 临时配置 | `%LOCALAPPDATA%\MobaDesk\rdp\*.rdp` |

`settings.json` 主要字段：

| 字段 | 取值 |
| --- | --- |
| `Theme` | `System` / `Dark` / `Light` |
| `Material` | `Mica` / `MicaAlt` / `Acrylic` / `Solid` |
| `Language` | `zh-CN` / `en-US` |
| `TerminalFontSize` / `ScrollbackLines` | 终端字号与回滚行数 |
| `MonitorIntervalSeconds` / `ConnectionTimeoutSeconds` | 监控刷新间隔、连接超时 |
| `CopyOnSelect` | 选中即复制 |
| `UpdateFeedUrl` | `version.json` 更新源（URL 或本地路径） |
| `CheckUpdatesOnStartup` | 启动时自动检查更新 |
| `LastUpdateCheckUtc` | 上次成功检查更新的 UTC 时间（用于 24 小时节流） |

## 架构

```
src/MobaDesk/
  Models/          会话、指标、设置等模型
  Services/        SSH.NET 会话、监控采集、FTP/SFTP 文件系统、RDP 启动器、DPAPI、更新检查
  Services/Localization.cs   运行时多语言加载器（从 Resources/Lang/*.json 读取）
  Services/JsonStore.cs      统一的 JSON 文件读写（源生成序列化、原子写入）
  Services/ThemeService.cs   主题与窗口材质（Mica / MicaAlt / Acrylic / Solid）
  Localization/    Loc 附加属性：XAML 文本、表头、占位符、提示的自动翻译
  Views/           SSH 终端、文件浏览器、监控面板、RDP 面板
  Dialogs/         新建/编辑会话、密码、设置
  Controls/        轻量实时折线图 Sparkline
  Assets/          xterm.js 终端页面、CodeMirror 编辑器页面与图标
  Resources/Lang/  语言资源：zh-CN.json / en-US.json
tests/MobaDesk.ServiceProbe/
                  服务层端到端探针（SSH / SFTP / FTP / RDP 文件 / 监控 / 更新清单）
installer/        Inno Setup 安装脚本、中文语言文件与打包脚本
```

- 终端：WebView2 + xterm.js，C# 与 JS 之间通过 base64 二进制消息传输，带输出批处理与背压。
- 编辑器：WebView2 + CodeMirror 5（离线内置在 `Assets/editor`，MIT 许可，见 `Assets/editor/LICENSE.txt`），C# 只负责读取/写入文件、编码与换行判定。
- 多语言：`Localization.T(key)` 取词，词条来自 `Resources/Lang/{zh-CN,en-US}.json`（按语言懒加载并缓存）；XAML 通过 `loc:Loc.Key` / `loc:Loc.Header` 等附加属性绑定，切换语言时 `Loc.ApplyTree` 就地刷新，无需重建窗口。
- 数据存储：`settings.json`、`profiles.json`、`folders.json` 与语言资源都通过 `JsonStore` 统一读写，使用源生成 JSON 元数据（裁剪 / AOT 安全）与临时文件原子写入。
- 主题 / 材质：`ThemeService` 设置 `RequestedTheme`、`SystemBackdrop`（Mica / DesktopAcrylic）与 DWM 标题栏配色，不支持时回退为纯色。
- SSH / SFTP：SSH.NET。
- FTP / FTPS：FluentFTP。
- RDP：Windows 自带 `mstsc.exe`。
- 监控：通过独立 SSH 通道执行一次 `/proc` 采集脚本，解析 CPU、内存、网络、磁盘与进程。

## 安全说明

- 密码使用 Windows DPAPI（CurrentUser）加密，只有当前 Windows 用户能解密。
- SSH 主机密钥会在首次连接时自动信任，并在终端标题栏显示 SHA-256 指纹。生产环境请自行核对指纹。
- RDP 密码以 DPAPI 密文写入生成的 `.rdp` 文件；文件位于当前用户的数据目录中。
- 所有连接均为客户端直连，不经过任何第三方服务器。

## 已知限制

- 服务器监控目前针对 **Linux**（读取 `/proc`、`df`、`ps`）；Windows 服务器会提示无法解析。
- RDP 会话在独立的 `mstsc` 窗口中运行，而不是内嵌到 MobaDesk 标签页内。
- SFTP / 监控面板会各自建立一条 SSH 连接；极少数限制单连接数的服务器可能需要改为复用终端连接。
- 并发输入只对 SSH 会话生效；SFTP / FTP 文件面板与 RDP 会话不参与。
- 并发输入开启时切换标签页不会中断镜像，但被排除的会话不会收到任何输入。
- 会话文件夹目前是**单层**结构，不支持嵌套子文件夹；文件夹顺序按创建顺序排列，暂不支持拖动排序。
- 文件夹的折叠状态只在本次运行内有效，重启后默认全部展开。
- 已有的 SSH 标签页在切换语言后即时更新界面文字，终端里已经输出的历史内容保持原样。

## 验证

仓库包含一个服务层探针，可对一台真实 SSH/FTP 服务器做端到端检查：

```powershell
dotnet run --project .\tests\MobaDesk.ServiceProbe -- 127.0.0.1 22 user password 21 ftpuser ftppassword
```

输出会覆盖 SSH 交互、SFTP 上传下载、FTP 上传下载、RDP 文件生成与 Linux 监控快照。

更新清单也可单独验证（`--download` 会同时校验安装包的 SHA-256）：

```powershell
dotnet run --project .\tests\MobaDesk.ServiceProbe -- --update "$(Get-Location)\installer\output\version.json" --download
```
