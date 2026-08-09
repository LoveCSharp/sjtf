# sjtf - 命令行骨架工具

![.NET](https://img.shields.io/badge/.NET-10.0-blue)
![License: MIT](https://img.shields.io/badge/License-MIT-green)

一个便携式 CLI 包管理器，用于从 GitHub 和其他源下载、校验和管理命令行工具。

## 功能特性

- 🚀 一键安装/卸载/升级命令行工具
- 🔍 基于 Lua 脚本的可扩展版本获取
- ✅ SHA-256/SHA-1/SHA-512/MD5 摘要校验
- 🔗 自动创建 shim（符号链接 / shell 脚本）
- 🌐 跨平台：Windows、Linux、macOS
- 🏗️ 支持 Native AOT 编译

## 快速开始

```bash
sjtf packages list    # 列出可用包
sjtf packages update  # 从远程更新 pkgs.json
sjtf list             # 列出已安装包
sjtf install fnm uv   # 安装包
sjtf uninstall fnm    # 卸载包
sjtf upgrade --all    # 升级所有已安装包
sjtf favorites        # 同步 favorites.json
sjtf --version        # 显示版本号
```

## 命令

| 命令 | 别名 | 说明 |
|---------|---------|-------------|
| `packages list` | `pkgs list`、`pkgs ls` | 列出 `pkgs.json` 中定义的所有包 |
| `packages update` | `pkgs update`、`pkgs up` | 从远程下载最新的 `pkgs.json` |
| `list` | `ls` | 列出已安装的包 |
| `install` | `i` | 安装一个或多个包 |
| `uninstall` | `u`、`rm`、`remove` | 卸载一个或多个包 |
| `upgrade` | `up` | 升级已安装的包到最新版本 |
| `favorites` | `favors` | 根据 `favorites.json` 同步已安装的包 |
| `--version` | | 显示版本信息 |

## 配置文件

所有配置文件位于可执行文件同级目录。

> **注意（Windows）：** 创建符号链接需要管理员权限或启用开发人员模式。否则 symlink 创建会失败。

### `config.toml`

主配置文件。首次运行时会自动生成，包含默认值。

```toml
[general]
install_dir = "D:\\sjtf_pkgs"     # 所有安装的根目录

[pkgs]
remote_url = "https://cdn.jsdelivr.net/gh/LoveCSharp/sjtf@main/sjtf/pkgs.json"  # `sjtf packages update` 使用的远程 pkgs.json URL

[download]
aria2_enable = true                # 是否启用 aria2 下载
max_connection_per_server = 10     # 每服务器最大连接数（1 ~ 16）
split = 10                         # 下载分块数（1 ~ 16）
min_split_size = 1                 # 最小分块大小，单位 MB（1 ~ 1024）

[aria2]
windows_x86_64 = "https://github.com/aria2/aria2/releases/download/release-1.37.0/aria2-1.37.0-win-64bit-build1.zip"

[github]
token_classic = "put your classic token here"  # GitHub 个人访问令牌（可选）
proxy = "https://gh-proxy.com"                 # GitHub 代理（可选）

[http.request.header]
user_agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 Edg/151.0.0.0"
```

### `pkgs.json`

包定义文件。使用 `sjtf packages update` 可从 `config.toml` 配置的远程 URL 下载最新版本。

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
    "pkg_install_relative_dir": "langs\\fnm",
    "shim": {
      "windows": {
        "symlink": ["fnm.exe"],
        "shell_script": {
          "fnm.cmd": "@\"{PKG_INSTALL_DIR}\\fnm.exe\" %*",
          "fnm.ps1": "& \"{PKG_INSTALL_DIR}\\fnm.exe\" @args"
        }
      }
    },
    "fetch_source": "github"
  }
}
```

#### shim 配置

`shim` 按操作系统分层，支持两种类型：

- `symlink`：字符串数组，创建指向安装目录内文件的符号链接，链接名从目标文件名自动推导
- `shell_script`：键值对对象，为每个键创建同名文件，内容为对应的值

`shell_script` 支持以下占位符：

| 占位符 | 替换为 |
|--------|--------|
| `{PKG_INSTALL_DIR}` | 包的完整安装路径（`config.install_dir` + `pkg_install_relative_dir`） |
| `{INSTALL_DIR}` | 全局安装根目录（`config.install_dir`） |

#### 包类型

| 类型 | 说明 |
|------|------|
| `portable-compressed-archive` | ZIP/TAR.GZ/7Z 压缩包 |
| `portable-exe` | 独立可执行文件 |
| `installer` | 安装程序（使用 `install_params` 参数执行） |

`installer` 类型支持以下字段：

| 字段 | 说明 |
|------|------|
| `install_params` | 安装程序参数，支持 `{PKG_INSTALL_DIR}` 和 `{INSTALL_DIR}` 占位符 |
| `uninstall_program` | 卸载程序文件名（相对于安装目录） |
| `uninstall_params` | 卸载程序参数，支持 `{PKG_INSTALL_DIR}` 和 `{INSTALL_DIR}` 占位符 |

### `installed.json`

自动生成的文件，记录已安装的包及其版本。首次执行安装/卸载/升级/list 等命令时会自动创建，无需手动创建。请勿手动编辑。

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

## 使用 Lua 脚本扩展

`sjtf` 支持通过 Lua 脚本自定义获取源和安装/卸载后处理。

- **获取源脚本**：`scripts/{fetch_source}_fetch_latest.lua`
- **安装后脚本**：`scripts/after_install/{os}/{arch}/{name}.lua`
- **卸载后脚本**：`scripts/after_uninstall/{os}/{arch}/{name}.lua`

详见 [Manual.md](Manual.md)。

## 构建

```bash
dotnet build
dotnet publish -c Release -r win-x64 --self-contained
```

## 许可证

MIT — 详见 [LICENSE](LICENSE) 文件。
