# sjtf - 命令行骨架工具

![.NET](https://img.shields.io/badge/.NET-10.0-blue)
![License: MIT](https://img.shields.io/badge/License-MIT-green)

一个便携式 CLI 包管理器，用于从 GitHub 和其他源下载、校验和管理命令行工具。

## 功能特性

- 🚀 一键安装/卸载/升级命令行工具
- 🔍 基于 Lua 脚本的可扩展版本获取
- ✅ SHA-256/SHA-1/SHA-512/MD5 摘要校验
- 🔗 自动创建符号链接
- 🌐 跨平台：Windows、Linux、macOS
- 🏗️ 支持 Native AOT 编译

## 快速开始

```bash
sjtf packages          # 列出可用包
sjtf list              # 列出已安装包
sjtf install fnm uv    # 安装包
sjtf uninstall fnm     # 卸载包
sjtf upgrade --all     # 升级所有已安装包
sjtf favorites         # 同步 favorites.json
sjtf --version         # 显示版本号
```

## 命令

| 命令 | 别名 | 说明 |
|---------|---------|-------------|
| `packages` | `pkgs` | 列出 `pkgs.json` 中定义的所有包 |
| `list` | `ls` | 列出已安装的包 |
| `install` | `i` | 安装一个或多个包 |
| `uninstall` | `u`、`rm`、`remove` | 卸载一个或多个包 |
| `upgrade` | `up` | 升级已安装的包到最新版本 |
| `favorites` | `favors` | 根据 `favorites.json` 同步已安装的包 |
| `--version` | | 显示版本信息 |

## 配置文件

所有配置文件位于可执行文件同级目录。

### `config.toml`

主配置文件。首次运行时会自动生成，包含默认值。

```toml
[general]
install_dir = "D:\\sjtf_pkgs"     # 所有安装的根目录
download_retry_max = 3             # 下载最大重试次数
create_symlink = true              # 是否创建符号链接（false 禁用）

[github]
token_classic = "put your classic token here"  # GitHub 个人访问令牌（可选）
proxy = "https://gh-proxy.com"                 # GitHub 代理（可选）

[http.request.header]
user_agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 Edg/151.0.0.0"  # HTTP 请求 User-Agent
```

### `pkgs.json`

包定义文件。

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

详见 [Manual-zh_cn.md](Manual-zh_cn.md)。

## 构建

```bash
dotnet build
dotnet publish -c Release -r win-x64 --self-contained
```

## 许可证

MIT — 详见 [LICENSE](LICENSE) 文件。
