# MobaDesk

WinUI 3 构建的 Windows 多协议服务器工作台（类似 MobaXterm），不限并发 SSH 连接数。开发进度见 [docs/PROGRESS.md](docs/PROGRESS.md)。

## 功能

| 模块 | 说明 |
| --- | --- |
| SSH 终端 | xterm.js（WebView2）：ANSI 颜色、回滚、自适应；`Ctrl+Shift+C` 复制、`Ctrl+V` 粘贴、`Ctrl+滚轮` 缩放；密码 / 私钥 / 键盘交互认证 |
| SFTP | 目录浏览、上传下载（含递归）、重命名删除、Unix 权限、按类型着色图标、右键菜单 |
| 文本编辑器 | 内嵌 CodeMirror，按扩展名自动高亮，行号 / 括号匹配，无写权限时只读 |
| FTP / FTPS | 显式 TLS，目录管理与传输 |
| RDP | 生成 `.rdp` 调原生 `mstsc`：NLA、分辨率、全屏、剪贴板、磁盘重定向 |
| 服务器监控 | CPU / 内存 / Swap / 负载 / 网络 / 磁盘 / 高 CPU 进程（Linux `/proc`） |
| 并发会话 | 多选后同一标签页排布（每行两列），键入 / 粘贴 / 命令实时镜像，多行命令栏统一下发 |
| 标签页 | 数量不限；右键关闭当前 / 其他 / 左侧 / 右侧 / 全部 |
| 凭据安全 | 密码与私钥口令 DPAPI（当前用户）加密，不落明文 |
| 会话管理 | 搜索、编辑、复制、删除；文件夹分组与拖拽 |
| 外观 | Mica / MicaAlt / Acrylic / 纯色；跟随系统 / 深 / 浅主题；简体中文 / English |

## 环境要求

- Windows 10 1809+ / Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/)
- WebView2 Runtime（Windows 11 自带）

## 构建与运行

```powershell
dotnet build .\src\MobaDesk\MobaDesk.csproj -c Debug
.\src\MobaDesk\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\MobaDesk.exe
```

> `WindowsAppSDKSelfContained=true`，无需预装 Windows App Runtime。

## 发布

```powershell
.\publish.ps1          # 自包含精简版（默认，~166 MB，复制即用）
.\publish.ps1 -Fx      # 依赖运行时（~43 MB，需 .NET 8 + WinAppRuntime 1.8）
.\publish.ps1 -R2R     # 自包含 + ReadyToRun（启动更快，体积更大）
.\publish.ps1 -Aot     # Native AOT（实验性，见下）
```

- 默认裁剪 Windows App SDK 自带的 AI 运行时与多余语言资源（省约 50 MB）。
- `-Aot` 与裁剪（`PublishTrimmed`）在 WinUI 下会启动崩溃（CsWinRT 需运行时代码生成），仅留档，勿用于生产。

## 安装包与自动更新

| 形态 | 命令 | 产物 |
| --- | --- | --- |
| Inno Setup | `.\installer\build-installer.ps1` | `installer\output\MobaDesk-Setup-<ver>.exe` + `version.json` |
| ClickOnce | `.\build-clickonce.ps1` | `publish\clickonce\`（`setup.exe` 引导） |
| 便携版 | `.\publish.ps1` 后压缩目录 | 解压即用，配合 `MOBADESK_DATA_DIR` 多实例 |

自动更新（仅 Inno Setup 安装包）：`version.json` 记录版本 / 下载地址 / SHA-256，比对版本 → 下载 → 校验 → `/VERYSILENT` 静默升级。`url` 可相对 `version.json` 所在目录。

## GitHub Actions 自动发布

推送 `v*` tag 即自动构建三种安装包并发布到 GitHub Release：

```powershell
# 1. 修改 src/MobaDesk/MobaDesk.csproj 的 <Version>
# 2. 提交后打 tag
git tag v1.0.1
git push origin v1.0.1
```

产物：`MobaDesk-Setup-<ver>.exe`、`MobaDesk-Portable-<ver>-win-x64.zip`、`MobaDesk-ClickOnce-<ver>.zip`、`version.json`。更新源固定为 `https://github.com/<owner>/<repo>/releases/latest/download/version.json`。

## 使用要点

- 左侧新建 SSH / SFTP / FTP / RDP；拖到文件夹分组。
- `Ctrl` / `Shift` 多选后双击 → 并发标签页，键入 / 粘贴 / 命令实时镜像；`Ctrl+Enter` 下发、`Enter` 换行。
- 终端：`Ctrl+Shift+C` 复制、`Ctrl+V` 粘贴、`Ctrl+Shift+F` 查找、`Ctrl+Shift+L` 清屏、`Ctrl+滚轮` 缩放、`Ctrl+T` 新建、`Ctrl+W` 关闭。
- SSH 页内开 SFTP / 监控侧栏；双击文件在新标签页编辑。
- 设置（主题 / 材质 / 语言 / 字号）即时生效。

## 数据位置

| 内容 | 路径 |
| --- | --- |
| 会话 / 分组 / 设置 | `%LOCALAPPDATA%\MobaDesk\profiles.json`、`folders.json`、`settings.json` |
| 日志 | `%LOCALAPPDATA%\MobaDesk\logs\app.log` |
| RDP 临时配置 | `%LOCALAPPDATA%\MobaDesk\rdp\*.rdp` |

## 架构

```
src/MobaDesk/
  Models/ Services/ Views/ Dialogs/ Controls/   业务模型、服务、界面
  Services/Localization.cs  JsonStore.cs  ThemeService.cs
  Localization/  Assets/  Resources/Lang/
tests/MobaDesk.ServiceProbe/   服务层端到端探针
installer/   Inno Setup 脚本与打包
```

- 终端：WebView2 + xterm.js，C#↔JS 走 base64 二进制消息，输出批处理 + 背压。
- 编辑器：WebView2 + CodeMirror 5（离线内置，MIT）。
- 存储：`JsonStore` 统一读写（源生成序列化 + 原子写入）。
- SSH / SFTP 用 SSH.NET；FTP / FTPS 用 FluentFTP；RDP 调 `mstsc.exe`；监控走独立 SSH 通道采集 `/proc`。

## 安全

- 密码 DPAPI（CurrentUser）加密，仅当前 Windows 用户可解密。
- SSH 主机密钥首次连接自动信任，标题栏显示 SHA-256 指纹（生产环境请自行核对）。
- 客户端直连服务器，不经过第三方；下载拒绝带路径分隔符 / `..` 的文件名，SFTP 写临时文件原子替换。

## 验证

```powershell
dotnet run --project .\tests\MobaDesk.ServiceProbe -- 127.0.0.1 22 user pass 21 ftpuser ftppass
dotnet run --project .\tests\MobaDesk.ServiceProbe -- --update .\installer\output\version.json --download
```

## 已知限制

- 监控仅支持 Linux（`/proc`）；Windows 服务器无法解析。
- RDP 在独立 `mstsc` 窗口中运行，不内嵌标签页。
- SFTP / 监控各自占用一条 SSH 连接；并发输入仅对 SSH 生效。
- 会话文件夹仅单层，不支持嵌套；折叠状态不跨重启。
