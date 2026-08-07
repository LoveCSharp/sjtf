# sjtf - 命令行骨架工具

一个便携式 CLI 包管理器，用于从 GitHub 和其他源下载、校验和管理命令行工具。

## 快速开始

```bash
sjtf packages list    # 列出可用包
sjtf packages update  # 从远程更新 pkgs.json
sjtf list             # 列出已安装包
sjtf install fnm uv    # 安装包
sjtf uninstall fnm     # 卸载包
sjtf upgrade --all     # 升级所有已安装包
sjtf favorites         # 同步 favorites.json
sjtf --version         # 显示版本号
```

## 命令

### `packages`（别名：`pkgs`）

包定义管理的父命令。使用子命令 `list` 或 `update`。

```bash
sjtf packages
sjtf pkgs
```

#### `packages list`（别名：`pkgs list`）

列出 `pkgs.json` 中定义的所有包。

```bash
sjtf packages list
sjtf pkgs list
```

#### `packages update`（别名：`pkgs update`）

从 `config.toml [pkgs].remote_url` 配置的远程 URL 下载最新的 `pkgs.json` 并覆盖本地文件。

```bash
sjtf packages update
sjtf pkgs update
```

### `list`（别名：`ls`）

列出所有已安装的包及其版本。

```bash
sjtf list
sjtf ls
```

### `install`（别名：`i`）

安装一个或多个包。如果包已安装且版本相同，则跳过。

```bash
sjtf install fnm
sjtf install fnm uv jq
sjtf i vscode
```

### `uninstall`（别名：`u`、`rm`、`remove`）

卸载一个或多个包。删除符号链接、安装目录，并在定义了卸载脚本时执行卸载脚本。

```bash
sjtf uninstall fnm
sjtf uninstall fnm uv jq
sjtf rm vscode
```

### `upgrade`（别名：`up`）

升级一个或多个已安装的包到最新版本。如果包未安装，则跳过。

```bash
sjtf upgrade fnm           # 升级单个包
sjtf upgrade fnm uv jq     # 升级多个包
sjtf upgrade --all         # 升级所有已安装包
sjtf up --all
```

### `favorites`（别名：`favors`）

根据 `favorites.json` 同步已安装的包。列表中的包会被安装/升级，不在列表中的包会被卸载。

```bash
sjtf favorites
sjtf favors
```

### `--version`

显示当前 sjtf 版本号。

```bash
sjtf --version
```

## 配置文件

所有配置文件位于可执行文件同级目录。

### `config.toml`

主配置文件。首次运行时会自动生成，包含默认值。

```toml
[general]
install_dir = "D:\\sjtf_pkgs"     # 所有安装的根目录
download_retry_max = 3             # 下载最大重试次数
create_symlink = true              # 是否创建符号链接（false 禁用）

[pkgs]
remote_url = "https://cdn.jsdelivr.net/gh/LoveCSharp/sjtf@main/sjtf/pkgs.json"  # `sjtf packages update` 使用的远程 pkgs.json URL

[github]
token_classic = "put your classic token here"  # GitHub 个人访问令牌（可选）
proxy = "https://gh-proxy.com"                 # GitHub 代理（可选）

[http.request.header]
user_agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 Edg/151.0.0.0"  # HTTP 请求 User-Agent
```

### `pkgs.json`

包定义文件。使用 `sjtf packages update` 可从 `config.toml` 配置的远程 URL 下载最新版本。

| 字段 | 说明 |
|---|---|
| `repo` | GitHub 仓库（`owner/name`） |
| `fetch_asset` | 资产匹配配置（架构、类型、正则） |
| `install_dir` | 相对安装目录 |
| `symlinks` | 符号链接名称 → 安装目录内相对路径的映射 |
| `fetch_source` | 获取源类型（`github`、`update_code_visualstudio_com` 等） |
| `script_after_install` | 设为 `true` 执行安装后 Lua 脚本 |
| `script_after_uninstall` | 设为 `true` 执行卸载后 Lua 脚本 |

**包类型：**

| 类型 | 说明 |
|---|---|
| `portable-compressed-archive` | ZIP/TAR.GZ/7Z 压缩包 |
| `portable-exe` | 独立可执行文件 |
| `installer` | 安装程序（使用 `install_params` 参数执行） |

**示例：**
```json
{
  "fnm": {
    "repo": "Schniz/fnm",
    "fetch_asset": {
      "arch": {
        "windows": {
          "x86_64": "(?=.*windows).*\\.zip$"
        }
      },
      "type": "portable-compressed-archive"
    },
    "install_dir": "langs\\fnm",
    "symlinks": {
      "fnm.exe": "fnm.exe"
    },
    "fetch_source": "github"
  }
}
```

### `installed.json`

自动生成的文件，记录已安装的包及其版本。首次执行安装/卸载/升级/list 等命令时会自动创建，无需手动创建。请勿手动编辑。

```json
{
  "fnm": "v1.39.0",
  "uv": "0.12.2"
}
```

### `favorites.json`

`favorites` 命令使用的 JSON 数组，包含包名列表。

```json
[
  "fnm",
  "uv",
  "jq",
  "vscode"
]
```

## 自定义获取源

### 概述

`sjtf` 通过 Lua 脚本实现可扩展的版本获取逻辑。每个获取源对应一个 Lua 脚本文件，位于 `scripts/` 目录下，命名为 `{fetch_source}_fetch_latest.lua`。

**为什么用 Lua 脚本？**

- 不同源的 API 格式各异（GitHub Releases、VS Code 更新 API、自定义服务器等）
- 无需修改 C# 代码即可支持新的获取源
- 可在脚本中自定义认证、代理、版本解析等逻辑

### 与 pkgs.json 的联动

在 `pkgs.json` 中，每个包通过 `fetch_source` 字段指定使用哪个获取源：

```json
{
  "fnm": {
    "fetch_source": "github"
  },
  "vscode": {
    "fetch_source": "update_code_visualstudio_com"
  }
}
```

程序执行时：
1. 读取 `fetch_source` 值（如 `"github"`）
2. 拼接脚本路径：`scripts/{fetch_source}_fetch_latest.lua`（如 `scripts/github_fetch_latest.lua`）
3. 执行 Lua 脚本，脚本必须设置全局 `result` 表，包含以下字段：

| 字段 | 说明 |
|---|---|
| `version` | 上游版本字符串（用于与 `installed.json` 比较） |
| `url` | 资源下载 URL |
| `digest` | 摘要值（可选，默认为空） |
| `digest_algorithm` | 摘要算法（可选，默认为 `"sha256"`） |

### Lua 脚本接收的全局变量

C# 在执行 Lua 脚本前会注入以下全局变量：

| 变量 | 类型 | 说明 |
|---|---|---|
| `pkg` | table | 当前包定义（从 `pkgs.json` 解析） |
| `config` | table | 配置（从 `config.toml` 解析） |
| `os` | string | 当前操作系统（`windows` / `linux` / `macos`） |
| `arch` | string | 当前架构（`x86_64` / `aarch64` / `arm`） |

### C# 注册的 Lua 函数

| 函数 | 说明 |
|---|---|
| `http_get(url, headers_table)` | 发送 HTTP GET 请求，返回响应体字符串。自动添加 `User-Agent` 头（来自 `config.toml`）。`headers_table` 中的 key 会覆盖默认值。 |
| `json_decode(json_string)` | 将 JSON 字符串解析为 Lua 表 |
| `regex_match(pattern, input)` | 正则匹配，返回布尔值 |

### 内置获取源

| fetch_source | 脚本 | 说明 |
|---|---|---|
| `github` | `scripts/github_fetch_latest.lua` | 调用 GitHub Releases API，解析最新版本和资产 |
| `update_code_visualstudio_com` | `scripts/update_code_visualstudio_com_fetch_latest.lua` | 调用 VS Code 更新 API，解析最新版本和下载链接 |

### 编写自定义 fetch_latest.lua

**步骤 1：** 在 `scripts/` 目录下创建 `{fetch_source}_fetch_latest.lua`

**步骤 2：** 脚本必须设置 `result` 全局表

**注意：** `version`、`url`、`digest` 不一定来自同一个 API 响应或 URL。版本信息可能来自元数据 API，下载 URL 需要根据版本拼接，摘要则需要单独请求下载链接获取。

**示例：** 一个自定义获取源，版本信息和下载 URL 来自不同 API

```lua
-- scripts/my_source_fetch_latest.lua

-- 第一步：获取最新版本号
local meta_url = pkg.meta_url
local meta_body = http_get(meta_url)
local meta = json_decode(meta_body)
local version = meta.latest_version

-- 第二步：根据版本号拼装下载 URL
local url = string.format(
    "https://example.com/download/%s/%s/%s",
    pkg.name,
    version,
    pkg.filename_template
)

-- 第三步：单独请求下载链接以获取摘要（如 ETag 或 SHA-256）
local head_body = http_get(url)
local head = json_decode(head_body)
local digest = head.sha256 or ""

-- 设置结果（必须）
result = {
    version = version,
    url = url,
    digest = digest,
    digest_algorithm = "sha256"
}
```

**对应 pkgs.json：**

```json
{
  "my_tool": {
    "fetch_source": "my_source",
    "meta_url": "https://example.com/api/meta",
    "filename_template": "my_tool-windows-x64.zip",
    "fetch_asset": {
      "type": "portable-compressed-archive"
    },
    "install_dir": "tools\\my_tool"
  }
}
```

**步骤 3：** 在 `pkgs.json` 中将包的 `fetch_source` 设置为脚本前缀名称（不含 `_fetch_latest.lua`）

### 注意事项

- 脚本执行失败时，错误信息会显示在控制台（通过异常链解包显示 Lua 错误）
- 脚本中可以使用标准 Lua 语法和库
- 如果脚本不存在或未设置 `result`，安装会报错并跳过该包

## 安装/卸载后脚本

### 概述

`sjtf` 支持在包安装完成或卸载完成后执行自定义 Lua 脚本，用于处理一些安装程序本身无法完成的后续工作（如创建包装脚本、清理残留文件等）。

### 与 pkgs.json 的联动

在 `pkgs.json` 中，通过两个布尔字段控制：

| 字段 | 说明 |
|---|---|
| `script_after_install` | 设为 `true`，安装完成后执行 `scripts/after_install/{os}/{arch}/{name}.lua` |
| `script_after_uninstall` | 设为 `true`，卸载完成后执行 `scripts/after_uninstall/{os}/{arch}/{name}.lua` |

**示例：**
```json
{
  "vscode": {
    "fetch_source": "github",
    "script_after_install": true,
    "script_after_uninstall": true
  }
}
```

### 脚本目录结构

```
scripts/
├── after_install/
│   └── {os}/
│       └── {arch}/
│           └── {name}.lua
└── after_uninstall/
    └── {os}/
        └── {arch}/
            └── {name}.lua
```

其中 `{os}` 为 `windows`、`linux` 或 `macos`，`{arch}` 为 `x86_64`、`aarch64` 或 `arm`。

### 可用全局变量

C# 在执行脚本前会注入以下全局变量：

| 变量 | 类型 | 说明 |
|---|---|---|
| `pkg` | table | 当前包定义（从 `pkgs.json` 解析） |
| `config` | table | 配置（从 `config.toml` 解析） |
| `os` | string | 当前操作系统 |
| `arch` | string | 当前架构 |
| `install_dir` | string | 包的完整安装目录路径 |
| `install_root` | string | 安装根目录（即 `config.toml` 中的 `install_dir`） |

### C# 注册的 Lua 函数

| 函数 | 说明 |
|---|---|
| `create_directory(path)` | 创建目录，成功返回 `nil`，失败返回错误字符串（仅 `after_install` 脚本可用） |
| `remove_file(path)` | 删除文件，成功返回 `nil`，失败返回错误字符串（仅 `after_uninstall` 脚本可用） |

### 编写脚本

**安装后脚本示例：** 为 VS Code 创建包装脚本并确保数据目录存在

```lua
-- scripts/after_install/windows/x86_64/vscode.lua

local code_cmd_path = install_dir .. "\\bin\\code.cmd"
local symlink_dir = install_root .. "\\symlink"
local output_path = symlink_dir .. "\\code.cmd"

-- 读取原始脚本内容
local f = io.open(code_cmd_path, "r")
if not f then error("cannot read " .. code_cmd_path) end
local content = f:read("*all")
f:close()

-- 替换相对路径为绝对路径
content = content:gsub("%%~dp0%.%.", install_dir)

-- 写入 symlink 目录
local out = io.open(output_path, "w")
if not out then error("cannot write " .. output_path) end
out:write(content)
out:close()

-- 创建数据目录
local data_dir = install_dir .. "\\data"
local err = create_directory(data_dir)
if err then
    io.stderr:write("warning: failed to create data directory: " .. err .. "\n")
end
```

**卸载后脚本示例：** 清理 VS Code 的包装脚本

```lua
-- scripts/after_uninstall/windows/x86_64/vscode.lua

local symlink_dir = install_root .. "\\symlink"
local output_path = symlink_dir .. "\\code.cmd"

local err = remove_file(output_path)
if err then
    io.stderr:write("warning: " .. err .. "\n")
end
```

### 注意事项

- 脚本路径必须严格匹配 `{os}/{arch}/{name}.lua`，否则会跳过执行
- 脚本执行失败不会阻止安装/卸载流程完成（错误会打印到 stderr）
- 卸载时 `install_dir` 仍可访问（目录尚未被删除），可用于清理该目录内创建的文件
- `install_root/symlink` 目录会在程序启动时自动创建，脚本中无需手动创建

## 目录结构

```
install_dir/
├── symlink/              # 符号链接和包装脚本
│   ├── fnm.exe
│   ├── uv.exe
│   ├── code.cmd
│   └── notepad3.cmd
├── langs/
│   ├── fnm/
│   └── uv/
├── cli/
│   ├── jq/
│   └── fzf/
└── editor/
    ├── vscode/
    └── notepad3/
```

## 支持的架构

| 操作系统 | 架构 | 值 |
|---|---|---|
| Windows | x64 | `windows` / `x86_64` |
| Windows | ARM64 | `windows` / `aarch64` |
| Linux | x64 | `linux` / `x86_64` |
| macOS | ARM64 | `macos` / `aarch64` |

## 摘要算法

| 算法 | 标识符 |
|---|---|
| SHA-256 | `sha256` |
| SHA-1 | `sha1` |
| SHA-512 | `sha512` |
| MD5 | `md5` |
