# MobaDesk 代码审查报告

- 审查日期：2026-11-22
- 审查范围：`src/`、`tests/` 全部源码（当前工作区，master 分支）
- 审查方式：`/code-review` 全量扫描 + 关键代码人工复核
- 结论：共发现 **10 个重要问题**，集中在 **安全传输（SSH/FTPS/RDP）**、**数据丢失风险** 和 **功能静默失效** 三类。其中 3 个属于安全缺陷，4 个可能导致用户数据丢失或损坏，建议优先修复。
- **修复状态（2026-11-22）：10/10 已全部修复，主工程与 ServiceProbe 均构建通过（0 警告 0 错误）。**

---

## 问题总览

| # | 严重度 | 类别 | 位置 | 问题摘要 | 状态 |
|---|--------|------|------|----------|------|
| 1 | 🔴 高 | 安全 | `Services/SshAuth.cs:72` | SSH 主机密钥无条件信任，无 known_hosts 校验 | ✅ 已修复 |
| 2 | 🔴 高 | 安全 | `Services/FileSystems/FtpRemoteFileSystem.cs:35` | FTPS 关闭证书校验（`ValidateAnyCertificate = true`） | ✅ 已修复 |
| 3 | 🔴 高 | 安全 | `Services/RdpLauncher.cs:39` | 生成的 .rdp 关闭 RDP 服务器身份验证警告 | ✅ 已修复 |
| 4 | 🟠 中高 | 数据损坏 | `Views/RemoteEditorView.xaml.cs:617` | 保存非 UTF-8 文件时静默损坏内容 | ✅ 已修复 |
| 5 | 🟠 中高 | 数据丢失 | `Services/JsonStore.cs:33` | 读取失败被当作空配置，下次保存覆盖原文件 | ✅ 已修复 |
| 6 | 🟠 中高 | 数据丢失 | `Views/RemoteEditorView.xaml.cs:512` | 关闭标签页可能绕过未保存确认（250ms 竞态） | ✅ 已修复 |
| 7 | 🟠 中 | 资源泄漏 | `Services/SshSession.cs:55` | 连接建立过程中关闭标签页泄漏 SSH 连接 | ✅ 已修复 |
| 8 | 🟠 中 | 功能失效 | `Views/SshTerminalView.xaml.cs:415` | 终端设置因 JSON 属性名大小写不匹配而全部不生效 | ✅ 已修复 |
| 9 | 🟠 中 | 功能失效 | `Services/MonitorService.cs:393` | `ps aux` 回退输出解析错误，Top 进程列表为空 | ✅ 已修复 |
| 10 | 🟡 低 | 构建失败 | `tests/MobaDesk.ServiceProbe/MobaDesk.ServiceProbe.csproj:27` | 探针工程缺少源文件链接，无法编译 | ✅ 已修复 |

---

## 一、安全类问题

### 1. SSH 主机密钥无条件信任

**位置**：`src/MobaDesk/Services/SshAuth.cs:68-78`

```csharp
client.HostKeyReceived += (_, e) =>
{
    e.CanTrust = true;   // 任何主机密钥都被接受
    ...
};
```

**问题**：`HostKeyReceived` 永远设置 `CanTrust = true`，没有 known_hosts 校验、首次连接确认，也没有密钥变更告警。状态栏显示的 SHA256 指纹是在连接已被接受之后才展示的，无法起到防中间人攻击的作用。

**影响**：攻击者在网络中冒充 SSH 服务器时，SSH.NET 会直接接受其主机密钥，随后客户端发送密码或私钥认证信息，会话即被完全劫持。

**修复建议**：
- 实现 TOFU（Trust On First Use）：把 `host:port -> SHA256 指纹` 持久化到数据目录（如 `known_hosts.json`）。
- 首次连接弹窗展示指纹由用户确认；后续连接指纹不匹配时中止并提示。
- 仅在用户显式确认后才更新记录。

---

### 2. FTPS 接受任意服务器证书

**位置**：`src/MobaDesk/Services/FileSystems/FtpRemoteFileSystem.cs:33-38`

```csharp
var config = new FtpConfig
{
    EncryptionMode = _profile.UseTls ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None,
    ValidateAnyCertificate = true,   // 任意证书均通过
    ...
};
```

**问题**：`ValidateAnyCertificate = true` 使 FluentFTP 跳过证书链、有效期和主机名校验，TLS 实际上失去了服务器身份认证能力。

**影响**：在不可信网络中，攻击者用任意自签名证书即可冒充 FTPS 服务器，获取 FTP 用户名、密码及传输的文件内容。

**修复建议**：
- 默认保持 `ValidateAnyCertificate = false`。
- 如确需支持自签名证书，做成显式的、带风险提示的用户选项，并支持固定（pin）指定指纹。

---

### 3. RDP 文件关闭服务器身份验证警告

**位置**：`src/MobaDesk/Services/RdpLauncher.cs:39`

```csharp
"authentication level:i:0",   // 0 = 连接时不验证服务器
```

**问题**：生成的 `.rdp` 文件使用 `authentication level:i:0`，即使服务器证书无效（过期、自签名、主机名不匹配）mstsc 也不会警告；同时文件内嵌 DPAPI 加密的凭据并设置 `prompt for credentials:i:0`。

**影响**：用户连接到伪装的 RDP 服务器时不会收到任何提示，保存的凭据可能被转发给攻击者。

**修复建议**：
- 改为 `authentication level:i:2`（服务器身份验证失败时不连接），这是三者中最严格的级别。
- 若必须兼容旧服务器，提供用户可配置项，而不是默认静默降级。

---

## 二、数据丢失 / 数据损坏类问题

### 4. 保存非 UTF-8 文本文件时静默损坏

**位置**：`src/MobaDesk/Views/RemoteEditorView.xaml.cs:613-619`

```csharp
return (new UTF8Encoding(false, false).GetString(bytes), new UTF8Encoding(false), false);
```

**问题**：`TryDecode` 对非 UTF-8 编码（如 GBK/GB18030/Latin-1）的文件使用带替换回退的 UTF-8 解码，非法字节被转换为 `U+FFFD`；保存时 `Encode` 再把这些替换字符写成 UTF-8，原始双字节内容永久丢失。

**影响**：通过 SFTP/FTP 打开 GBK 编码的 `.ini`/`.conf`（含中文注释），修改一行并 Ctrl+S 后，远端文件所有非 ASCII 内容被替换为 `EF BF BD`，且没有任何提示。

**修复建议**：
- 使用严格的 UTF-8 解码：`new UTF8Encoding(false, true)`，捕获 `DecoderFallbackException`。
- 对非 UTF-8 文件拒绝编辑/保存并提示，或引入编码检测（BOM/chardet）并在保存时按原编码回写。

---

### 5. 配置读取失败被当作空配置并覆盖原文件

**位置**：`src/MobaDesk/Services/JsonStore.cs:20-40`

```csharp
catch (Exception ex)
{
    Log.Warn($"Failed to read JSON '{path}': {ex.Message}");
    return false;   // 调用方视为"无数据"
}
```

**问题**：`TryRead` 把所有异常（JSON 解析失败、IO 错误、权限问题）都折叠为"读取失败"，`ProfileStore.LoadProfiles` 因此得到空列表，`AppSettings.Load` 得到默认值；后续保存（关窗保存 profiles、修改设置保存 settings）会把空/默认状态写回原文件。

**影响**：`profiles.json` 一旦损坏或临时不可读，下次启动侧边栏为空，关闭窗口时 `ProfileStore.Save()` 写入 `[]`，所有已保存的会话永久丢失。`settings.json` 同理。

**修复建议**：
- 区分 `FileNotFoundException`（正常空状态）与其他异常（解析/IO 错误）。
- 读取失败时进入只读保护状态：备份原文件（如 `.bak`）、禁止自动保存，并向用户显示错误。
- 保存前可做一次"空数据覆盖非空文件"的保护检查。

---

### 6. 关闭编辑器可能绕过未保存确认

**位置**：`src/MobaDesk/Views/RemoteEditorView.xaml.cs:512-517`（`ConfirmCloseAsync`）

**问题**：`_dirty` 只在最后一次变更通知后约 250ms 才重新计算。`ConfirmCloseAsync` 直接信任 `_dirty`，在这个时间窗口内关闭标签页会被当作"文件未修改"。

**影响**：在编辑器中输入一个字符后 250ms 内按 Ctrl+W，`_dirty` 仍为 `false`，标签页直接关闭，编辑内容丢失且没有"保存/放弃/取消"对话框。

**修复建议**：
- 关闭时（以及窗口退出时）直接比较编辑器当前文本与 `_loadedText`，而不是依赖延迟标记。
- 或在文本变更事件中立即置 `_dirty = true`，防抖只用于其他 UI 更新。

---

## 三、资源泄漏 / 功能失效类问题

### 7. 连接建立过程中关闭标签页泄漏 SSH 连接

**位置**：`src/MobaDesk/Services/SshSession.cs:25-56`

```csharp
var (client, shell) = await Task.Run(async () => { ... }, cancellationToken);
_client = client;
_shell = shell;
...
_cts = new CancellationTokenSource();
_ = Task.Run(() => ReadLoop(_cts.Token));
```

**问题**：`Dispose()` 只设置 `_disposed` 并清空字段，但已在执行的 `ConnectAsync` 不会被取消。连接任务完成后仍无条件赋值 `_client`/`_shell` 并启动 `ReadLoop`；`ReadLoop` 因 `_disposed == true` 立即退出，但新建立的 client/shell 永远不会被释放。

**影响**：连接到响应缓慢或不可达的主机后，在握手完成前关闭标签页——后台连接成功建立，但会话在服务器端保持、客户端 socket 直到进程退出才释放。

**修复建议**：
- 在 `Task.Run` 返回后检查 `_disposed`，若已释放则立即 `Dispose()` 新建的 client/shell 并返回。
- `Dispose()` 中取消连接用的 CancellationTokenSource。
- `MonitorService.ConnectAsync` 存在同样模式，一并修复。

---

### 8. 终端字体/回滚等设置全部不生效

**位置**：`src/MobaDesk/Views/SshTerminalView.xaml.cs:412-424`

**问题**：`TerminalConfigureOptions` 通过源生成的 `MobaDeskJsonContext` 序列化，未设置命名策略，输出为 PascalCase（`FontSize`、`FontFamily`、`Scrollback`、`CopyOnSelect`）；而 `terminal.html` 的 `configure(options)` 读取的是 `options.fontSize`、`options.fontFamily`、`options.scrollback`、`options.copyOnSelect`。属性名不匹配，所有配置被静默忽略。

**影响**：在设置中修改终端字体大小、字体、回滚行数或"选中即复制"，打开/重连 SSH 标签页后仍是硬编码的 14px 字体、8000 行回滚和 `copyOnSelect = false`；Ctrl+滚轮调整的字号也不会在下一个终端恢复。

**修复建议**：
- 给四个属性添加 `[JsonPropertyName("fontSize")]` 等特性，或为该类型使用 camelCase 命名策略。
- 增加一个测试，断言序列化后的键名与前端读取的键名一致。

---

### 9. `ps aux` 回退解析错误，Top 进程列表为空

**位置**：`src/MobaDesk/Services/MonitorService.cs:393-397`

```csharp
var extendedStyle = lines[0].Contains("PID", ...) &&
                    lines[0].Contains("%CPU", ...);
```

**问题**：procps 的 `ps aux` 表头同样包含 `PID` 和 `%CPU`，因此被误判为扩展格式，解析器按"第一列是 PID"处理，而 `ps aux` 第一列是 `USER`。所有 `int.TryParse(parts[0])` 失败，进程列表为空。（下方 `else` 分支的 `ps aux` 注释和列索引实际不可达。）

**影响**：在 Alpine/BusyBox 等 `ps -eo ... --sort` 不可用、只能回退到 `ps aux` 的主机上，监控面板的 Top CPU 进程列表始终为空。

**修复建议**：
- 通过首列是否为 `USER`（或表头是否以 `USER` 开头）识别 `ps aux` 格式，而不是仅凭 `PID`/`%CPU` 判断。
- 或让脚本显式输出格式标记（如 `@@PROC_EXT` / `@@PROC_AUX`），解析端据此选择列索引。
- 为两种格式各加一个解析单元测试。

---

### 10. 服务探针工程无法编译

**位置**：`tests/MobaDesk.ServiceProbe/MobaDesk.ServiceProbe.csproj:27-43`

**问题**：探针工程通过 `<Compile Include=... Link=...>` 引用主工程源文件，但缺少：
- `src/MobaDesk/Services/JsonStore.cs`
- `src/MobaDesk/Models/FileIconPalette.cs`

而 `AppSettings.cs`、`Localization.cs`、`RemoteEntry.cs` 依赖这两个文件，导致编译报 5 个 `CS0103`（`JsonStore` ×3、`FileIconPalette` ×2）。

**影响**：`dotnet build MobaDesk.slnx` 失败，`docs/PROGRESS.md` 中描述的端到端探针流程完全无法执行。

**修复建议**：
- 在 ItemGroup 中补上两个 `<Compile Include=... Link=...>` 条目。
- 重新运行 `dotnet build` 与探针，确认通过。

---

## 建议修复顺序

1. **第一批（安全，尽快）**：#1 SSH 主机密钥、#2 FTPS 证书校验、#3 RDP 认证级别。
2. **第二批（数据安全）**：#5 配置读取失败保护、#4 非 UTF-8 保存、#6 未保存确认竞态。
3. **第三批（功能与构建）**：#10 恢复构建 → #8 终端设置 → #9 进程解析 → #7 SSH 连接泄漏。

> 说明：以上问题均已对照源码逐条复核（行号以当前工作区为准）。修复 #10 后建议先跑通完整构建，再按批次补充回归测试。

---

## 修复记录（2026-11-22）

| # | 修改内容 |
|---|----------|
| 1 | 新增 `Services/HostKeyVerifier.cs` 与 `known_hosts.json`（TOFU）；`SshAuth.AttachHostKeyHandler` 统一校验 `SshClient`/`SftpClient`；首次连接或指纹变化时弹出确认框，指纹改用 `FingerPrintSHA256`；未配置校验器时默认拒绝。 |
| 2 | FTPS `ValidateAnyCertificate = false`。 |
| 3 | `.rdp` 改为 `authentication level:i:2`。 |
| 4 | 编辑器严格 UTF-8 解码；非 UTF-8/UTF-16 文件拒绝打开并给出 `Editor.EncodingUnsupported` 提示，不再回写 U+FFFD。 |
| 5 | `JsonStore.ReadStatus` 区分文件缺失与读取失败；损坏文件自动备份为 `.corrupt-*.bak`；`ProfileStore`/`AppSettings` 在读取失败时拒绝保存，并在启动时弹窗提示。 |
| 6 | 编辑器 change 事件立即置脏；`ConfirmCloseAsync`/`ReloadAsync` 关闭前重新比对实时文本；加载内容期间抑制脏标记。 |
| 7 | `SshSession` 在连接握手完成后检查 `_disposed` 并释放新连接，状态赋值加锁；`MonitorService.ConnectAsync` 同样处理。 |
| 8 | `TerminalConfigureOptions` 增加 camelCase `[JsonPropertyName]`。 |
| 9 | 通过表头首列（`USER` / `PID`）区分 `ps aux` 与 `ps -eo`，修复回退解析。 |
| 10 | 探针工程补入 `JsonStore.cs`、`FileIconPalette.cs` 链接；探针显式设置测试用 host key 校验器。 |

构建验证：
- `dotnet build src/MobaDesk/MobaDesk.csproj -c Debug` → 0 警告，0 错误
- `dotnet build tests/MobaDesk.ServiceProbe/MobaDesk.ServiceProbe.csproj -c Debug` → 0 警告，0 错误
- 完整解决方案构建仅因正在运行的 `MobaDesk.exe` 锁定输出文件而无法覆盖 exe；代码编译阶段已通过。
