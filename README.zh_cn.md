# sjtf - 命令行骨架工具

[English](README.md) | [中文](README.zh_cn.md)

![.NET](https://img.shields.io/badge/.NET-10.0-blue)
![Version](https://img.shields.io/badge/version-0.0.3-blue)
![License: MIT](https://img.shields.io/badge/License-MIT-green)

一个便携式 CLI 包管理器，用于从 GitHub 和其他源下载、校验和管理命令行工具。

## 功能特性

- 🚀 一键安装/卸载/升级命令行工具
- 🔌 基于 JavaScript 的可扩展获取源（内嵌 [Jint](https://github.com/sebastienros/jint)，支持 async/await）
- ✅ SHA-256/SHA-1/SHA-512/MD5 摘要校验
- 🔗 自动创建 shim（符号链接 / shell 脚本）
- 🌐 跨平台：Windows、Linux、macOS
- 🪝 安装/升级前后、卸载前后共六种 JS 钩子
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

| 命令 | 别名 | 参数 | 说明 |
|---------|---------|-----------|-------------|
| `packages list` | `pkgs list`、`pkgs ls` | — | 列出 `pkgs.json` 中定义的所有包 |
| `packages update` | `pkgs update`、`pkgs up` | — | 从远程下载最新的 `pkgs.json` |
| `list` | `ls` | — | 列出已安装的包 |
| `install` | `i` | `<name...>` | 安装一个或多个包 |
| `uninstall` | `u`、`rm`、`remove` | `<name...>` | 卸载一个或多个包 |
| `upgrade` | `up` | `[<name...>] \| --all` | 升级已安装的包到最新版本 |
| `favorites` | `favors` | — | 根据 `favorites.json` 同步已安装的包 |
| `--version` | | — | 显示版本信息 |

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
user_agent = "Mozilla/5.0 (Windows NT 10.0; Win64, x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 Edg/151.0.0.0"
```

### `pkgs.json`

包定义文件。使用 `sjtf packages update` 可从 `config.toml` 配置的远程 URL 下载最新版本。

```json
{
  "fnm": {
    "description": "Fast and simple Node.js version manager",
    "repo": "Schniz/fnm",
    "fetch_asset": {
      "arch": {
        "windows": {
          "x86_64": {
            "file": "^(?=.*windows)(?=.*x86_64).*\\.zip$",
            "type": "portable-compressed-archive"
          }
        }
      }
    },
    "pkg_install_relative_dir": "langs\\fnm",
    "shim": {
      "windows": {
        "symlink": {
          "fnm.exe": "fnm.exe"
        }
      }
    },
    "fetch_source": "github"
  }
}
```

#### shim 配置

`shim` 按操作系统分层，仅在当前操作系统匹配时生效。

- `symlink`：键值对对象。**键** 为 `shims/` 下的符号链接文件名，**值** 为包安装目录内的相对目标路径。
- `shell_script`：键值对对象。为每个键创建同名文件，内容为对应的值。支持 `{PKG_INSTALL_DIR}` 和 `{INSTALL_DIR}` 占位符。文件以 UTF-8 无 BOM 写入。

```json
"shim": {
  "windows": {
    "symlink": {
      "fnm.exe": "fnm.exe"
    },
    "shell_script": {
      "fnm.cmd": "@\"{PKG_INSTALL_DIR}\\fnm.exe\" %*",
      "fnm.ps1": "& \"{PKG_INSTALL_DIR}\\fnm.exe\" @args"
    }
  }
}
```

#### 包类型

| 类型 | 说明 |
|------|------|
| `portable-compressed-archive` | ZIP/TAR.GZ/7Z 压缩包，自动解压到安装目录 |
| `portable-executable` | 独立可执行文件，直接复制到安装目录 |
| `installer` | 安装程序，执行 `install_params` 指定的参数 |

每个 `arch.{os}.{arch}` 条目是一个对象，包含 `file`（资产 URL 或正则）和 `type`，以及 `installer` 的可选字段：

| 字段 | 说明 |
|------|------|
| `file` | 资产 URL（静态端点）或用于匹配 release 资产名的 JavaScript 正则 |
| `type` | 上表中的包类型之一 |
| `install_program` | 安装程序可执行文件。支持占位符 `{DOWNLOADED_CACHE_FILE_FULL_PATH}`（替换为缓存文件绝对路径）；其他值按原值使用（仅 `installer`） |
| `install_params` | 安装程序参数，支持 `{PKG_INSTALL_DIR}` 和 `{INSTALL_DIR}` 占位符（仅 `installer`） |
| `uninstall_program` | 卸载程序文件名（相对于安装目录，仅 `installer`） |
| `uninstall_params` | 卸载程序参数，支持 `{PKG_INSTALL_DIR}` 和 `{INSTALL_DIR}` 占位符（仅 `installer`） |

> **注：** 使用 `update_code_visualstudio_com` fetch 源的包把 `file` 字段替换为 `stable_latest_info_url`，该字段指向 VS Code 的更新元数据 API（返回 JSON，含真实下载 URL、`productVersion` 和 `sha256hash`）。示例见 `sjtf/pkgs.json` 中的 vscode 条目。

#### 包级字段

除了 `fetch_asset.arch.{os}.{arch}` 下的字段外，包对象顶层还支持以下字段：

| 字段 | 说明 |
|---|---|
| `file_mode_0755` | （仅 Linux/macOS）文件路径数组（相对于 `pkg_install_relative_dir`），安装完成后给这些文件设置 `0755` 权限。Windows 上为 no-op。 |

#### 安装前/后、升级前/后、卸载前/后脚本

将 JavaScript 钩子放到 `scripts/hooks/{name}-{os}-{arch}-before_install.js`（或 `-after_install.js` / `-before_upgrade.js` / `-after_upgrade.js` / `-before_uninstall.js` / `-after_uninstall.js`）即可自动执行。六种钩子彼此独立——各自仅在对应操作时触发，文件缺失时静默跳过。详见 [SCRIPTS.zh_cn.md](SCRIPTS.zh_cn.md)。

### `installed.json`

自动生成的文件，记录已安装的包及其版本。首次执行安装/卸载/升级/list 等命令时会自动创建，无需手动创建。请勿手动编辑。

### `favorites.json`

`favorites` 命令使用的 JSON 数组，包含包名列表。

完整默认列表见 `sjtf/favorites.json`（38 个条目），以下为片段示例：

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

## 编写 GitHub 源包

详见 [GITHUB_PKG.zh_cn.md](GITHUB_PKG.zh_cn.md)，了解如何编写 `fetch_source: "github"` 的包，包括 `pkgs.json` 字段说明、资产正则匹配、shim 配置以及完整安装流程。关于内置的 VS Code 更新 API 等非 GitHub 源，请参见 [SCRIPTS.zh_cn.md](SCRIPTS.zh_cn.md)。

## 使用 JavaScript 脚本扩展

`sjtf` 支持通过 JavaScript 脚本自定义获取源和安装/卸载后处理。

- **内置获取源**：`github`、`update_code_visualstudio_com`（新增自定义源见 [SCRIPTS.zh_cn.md](SCRIPTS.zh_cn.md)）
- **获取源脚本**：`scripts/fetch/{fetch_source}_fetch_latest.js`
- **安装前钩子**：`scripts/hooks/{name}-{os}-{arch}-before_install.js`
- **安装后钩子**：`scripts/hooks/{name}-{os}-{arch}-after_install.js`
- **升级前钩子**：`scripts/hooks/{name}-{os}-{arch}-before_upgrade.js`
- **升级后钩子**：`scripts/hooks/{name}-{os}-{arch}-after_upgrade.js`
- **卸载前钩子**：`scripts/hooks/{name}-{os}-{arch}-before_uninstall.js`
- **卸载后钩子**：`scripts/hooks/{name}-{os}-{arch}-after_uninstall.js`

详见 [SCRIPTS.zh_cn.md](SCRIPTS.zh_cn.md)。

## 构建

```bash
dotnet build
dotnet publish -c Release -r win-x64 --self-contained
```

## 许可证

MIT — 详见 [LICENSE](LICENSE) 文件。

> 本项目使用 [aria2](https://aria2.github.io/) 作为多线程下载引擎，aria2 为开源软件，遵循其相应开源许可证。
