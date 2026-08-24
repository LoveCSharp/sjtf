# 编写 GitHub 源包

[English](GITHUB_PKG.md) | [中文](GITHUB_PKG.zh_cn.md)

本文档详细介绍如何编写一个基于 GitHub Releases 的 sjtf 包定义。

## 概述

基于 GitHub 的包需要两个部分：

1. **`pkgs.json` 中的包定义**：描述包的基本信息、资产匹配规则、安装目录和 shim 配置
2. **`fetch_source` 指向 `github`**：使用内置的 `scripts/fetch/github_fetch_latest.js` 自动从 GitHub Releases API 获取最新版本

`sjtf` 内置了对 GitHub Releases API 的支持，无需编写自定义 JavaScript 脚本即可使用。

## 完整示例

以下是一个完整的包定义示例（以 `fnm` 为例）：

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

## 字段详解

### `repo`（必需）

GitHub 仓库标识，格式为 `owner/name`。

```json
"repo": "Schniz/fnm"
```

`sjtf` 会将其拼接为 GitHub API URL：
```
https://api.github.com/repos/Schniz/fnm/releases/latest
```

### `fetch_asset`（必需）

资产匹配配置，用于从 GitHub Release 的 assets 列表中选择正确的文件。

#### `fetch_asset.arch`（必需）

按操作系统和架构分层的映射，每个叶节点是一个对象，包含 `file` 和 `type`，以及 `installer` 的可选字段：

```json
"arch": {
  "windows": {
    "x86_64": {
      "file": "^(?=.*windows)(?=.*x86_64).*\\.zip$",
      "type": "portable-compressed-archive"
    }
  },
  "linux": {
    "x86_64": {
      "file": "^(?=.*linux)(?=.*x86_64).*\\.tar.gz$",
      "type": "portable-compressed-archive"
    }
  },
  "macos": {
    "aarch64": {
      "file": "^(?=.*macos)(?=.*aarch64).*\\.zip$",
      "type": "portable-compressed-archive"
    }
  }
}
```

结构：`fetch_asset.arch[os][arch]` 是一个对象，字段如下：

| 字段 | 说明 |
|------|------|
| `os` | `windows` / `linux` / `macos` — 操作系统 |
| `arch` | `x86_64` / `aarch64` / `arm` — 架构 |
| `file` | 资产 URL（源返回静态下载链接时）或用于匹配 release 资产 `name` 的 JavaScript 正则。非 URL 源使用 JavaScript `RegExp` 语法，命中首个匹配项。 |
| `type` | 下表中的包类型之一 |
| `install_program` | （可选，仅 `installer`）安装程序可执行文件。支持占位符 `{DOWNLOADED_CACHE_FILE_FULL_PATH}`（C# 在安装时替换为缓存文件绝对路径）；其他值按原值使用。 |
| `install_params` | （可选，仅 `installer`）安装程序参数，支持 `{PKG_INSTALL_DIR}` 和 `{INSTALL_DIR}` 占位符 |
| `uninstall_program` | （可选，仅 `installer`）卸载程序文件名，相对于安装目录 |
| `uninstall_params` | （可选，仅 `installer`）卸载程序参数，支持 `{PKG_INSTALL_DIR}` 和 `{INSTALL_DIR}` 占位符 |

> **注：** 使用 `update_code_visualstudio_com` fetch 源的包把 `file` 字段替换为 `stable_latest_info_url`，该字段指向 VS Code 的更新元数据 API。API 返回 JSON，包含真实下载 URL、`productVersion` 和 `sha256hash`。示例见 `data/pkgs.json` 中的 vscode 条目——或其源码片段 `sjtf.pkgs/pkg‑fragments/` 下的对应文件。

#### `fetch_asset.type`（必需）

包类型，决定安装时的处理方式：

| 类型 | 说明 |
|------|------|
| `portable-compressed-archive` | ZIP/TAR.GZ/7Z 压缩包，自动解压到安装目录 |
| `portable-executable` | 独立可执行文件，直接复制到安装目录 |
| `installer` | 安装程序，执行 `install_params` 指定的参数 |

### `pkg_install_relative_dir`（必需）

包在安装根目录下的相对路径。最终完整路径为 `config.install_dir + pkg_install_relative_dir`。

```json
"pkg_install_relative_dir": "langs\\fnm"
```

如果 `config.toml` 中 `install_dir = "D:\\sjtf_pkgs"`，则 fnm 的完整安装路径为：
```
D:\sjtf_pkgs\langs\fnm
```

### `shim`（可选）

Shim 配置，按操作系统分层。仅在当前操作系统匹配时生效。

```json
"shim": {
  "windows": {
    "symlink": {
      "fnm.exe": "fnm.exe",
      "jcode.exe": "jcode-windows-x86_64.exe"
    },
    "shell_script": {
      "fnm.cmd": "@\"{PKG_INSTALL_DIR}\\fnm.exe\" %*",
      "fnm.ps1": "& \"{PKG_INSTALL_DIR}\\fnm.exe\" @args"
    }
  }
}
```

#### `shim[os].symlink`

键值对对象。**键** 为 `shims/` 下的符号链接文件名，**值** 为包安装目录内的相对目标路径。

```json
"symlink": {
  "fnm.exe": "fnm.exe",
  "jcode.exe": "jcode-windows-x86_64.exe"
}
```

上述配置会创建：
- `shims/fnm.exe` -> `langs\fnm\fnm.exe`
- `shims/jcode.exe` -> `ai\jcode\jcode-windows-x86_64.exe`

#### `shim[os].shell_script`

键值对对象，为每个键创建同名文件，内容为对应的值。支持占位符：

| 占位符 | 替换为 |
|--------|--------|
| `{PKG_INSTALL_DIR}` | 包的完整安装路径 |
| `{INSTALL_DIR}` | 全局安装根目录（`config.install_dir`） |

```json
"shell_script": {
  "fnm.cmd": "@\"{PKG_INSTALL_DIR}\\fnm.exe\" %*",
  "fnm.ps1": "& \"{PKG_INSTALL_DIR}\\fnm.exe\" @args"
}
```

上述配置会创建：
- `shims/fnm.cmd`：Windows 批处理脚本
- `shims/fnm.ps1`：PowerShell 脚本

文件以 UTF-8 无 BOM 格式写入。

### 包级字段

除了 `fetch_asset.arch.{os}.{arch}` 下的字段外，包对象顶层还支持以下字段：

| 字段 | 说明 |
|---|---|
| `file_mode_0755` | （仅 Linux/macOS）文件路径数组（相对于 `pkg_install_relative_dir`），安装完成后给这些文件设置 `0755` 权限。Windows 上为 no-op。 |

### `fetch_source`（必需）

指定版本获取源。GitHub Releases 使用内置的 `github` 源：

```json
"fetch_source": "github"
```

对应脚本路径：`scripts/fetch/github_fetch_latest.js`

该脚本会：
1. 调用 `https://api.github.com/repos/{repo}/releases/latest`
2. 解析 `tag_name` 作为版本号
3. 使用 `fetch_asset.arch[os][arch]` 正则匹配 assets 列表
4. 提取 `browser_download_url` 作为下载地址
5. 提取 `digest` 作为摘要（GitHub API 返回的资产 digest 格式为 `sha256:abc123...`）

### 安装/升级/卸载的前后钩子

钩子按路径**自动检测**：把 JavaScript 文件放到约定路径下 `sjtf` 就会执行，无需在 `pkgs.json` 中配置任何字段。六种钩子彼此独立——任一文件缺失时静默跳过。

- 安装前：`scripts/hooks/{name}-{os}-{arch}-before_install.js`
- 安装后：`scripts/hooks/{name}-{os}-{arch}-after_install.js`
- 升级前：`scripts/hooks/{name}-{os}-{arch}-before_upgrade.js`
- 升级后：`scripts/hooks/{name}-{os}-{arch}-after_upgrade.js`
- 卸载前：`scripts/hooks/{name}-{os}-{arch}-before_uninstall.js`
- 卸载后：`scripts/hooks/{name}-{os}-{arch}-after_uninstall.js`

完整的钩子编写指南见 [SCRIPTS.zh_cn.md](SCRIPTS.zh_cn.md)。

## 使用 `pkgs_custom.json` 进行覆盖

可选的 `data/pkgs_custom.json` 允许你在不修改 `pkgs.json` 的前提下，新增或覆盖包定义。加载时，`Packages.Load()` 会在内存中合并两个文件：

- 若 `pkgs_custom.json` 缺失，则只使用 `pkgs.json`。
- 同名包会被 `pkgs_custom.json` 中的条目**完全替换**（不做字段级合并，整个包对象被覆盖）。
- 合并完全在内存中完成，`pkgs_custom.json` 永远不会被修改。
- `sjtf packages update` 只刷新 `pkgs.json`，自定义内容会保留。

在 `packages list` 与 `list` 的输出中，自定义覆盖包会在名称末尾追加 `*c` 标记（如 `ouch*c`）。

典型用途：

- 锁定特定版本的包（覆盖 `repo` 或资产规则）。
- 自定义描述、安装目录或 shim 路径。
- 添加私有包而无需 fork `pkgs.json`。

## 工作流程

当用户执行 `sjtf install fnm` 时：

1. **版本获取**：执行 `scripts/fetch/github_fetch_latest.js`
   - 调用 GitHub API 获取最新 Release
   - 匹配资产正则，提取下载 URL 和版本号
   - 返回一个 JSON 对象，包含：`version`（必需）、`url`（必需）、`type`（必需，取自 `fetch_asset.arch.{os}.{arch}.type`），以及可选的 `digest`、`digest_algorithm`（默认 `"sha256"`）、`install_program`、`install_params`、`uninstall_program`、`uninstall_params` —— 全部由 `ScriptFetchSource.cs` 解析为 `DownloadPlan`

2. **下载**：使用多线程分块下载（或 aria2c）到缓存目录
   - 缓存文件名格式：`{name}-{os}-{arch}-{version}.{ext}`

3. **验证**：计算下载文件的摘要，与 GitHub API 返回的 digest 比对
   - 不匹配则立即删除文件并重新下载一次
   - 再次不匹配则报错

4. **安装**：根据 `fetch_asset.arch.{os}.{arch}.type` 处理
   - `portable-compressed-archive`：解压到 `pkg_install_relative_dir`
   - `portable-executable`：复制到 `pkg_install_relative_dir`
   - `installer`：执行安装程序

5. **创建 shim**：根据 `shim[os]` 创建符号链接或 shell 脚本

6. **安装后钩子**（自动检测）：如果存在 `scripts/hooks/{name}-{os}-{arch}-after_install.js` 则执行

## 正则表达式示例

根据资产命名规则编写匹配正则：

| 资产名示例 | 匹配正则 | 说明 |
|-----------|---------|------|
| `fnm-windows-x86_64.zip` | `(?=.*windows)(?=.*x86_64).*\.zip$` | 同时包含 windows 和 x86_64 的 zip |
| `uv-aarch64-apple-darwin.tar.gz` | `(?=.*aarch64)(?=.*darwin).*\.tar\.gz$` | macOS ARM64 |
| `jq-windows-amd64.exe` | `(?=.*windows)(?=.*amd64).*\.exe$` | Windows x64 exe |
| `rg-x86_64-unknown-linux-musl.tar.gz` | `(?=.*x86_64)(?=.*linux).*\.tar\.gz$` | Linux x64 |

技巧：
- 使用 `(?=.*keyword)` 正向预查确保多个关键词同时出现
- 使用 `\.` 转义点号匹配文件扩展名
- 使用 `$` 锚定结尾

## GitHub API 认证

在 `config.toml` 中可以配置 GitHub 认证信息，以提高 API 速率限制：

```toml
[github]
token_classic = "ghp_xxxxxxxxxxxx"  # GitHub 个人访问令牌
proxy = "https://gh-proxy.com"       # 可选代理
```

- `token_classic`：GitHub 经典个人访问令牌，必须以 `ghp_` 开头
- `proxy`：代理地址，用于替换 GitHub API 请求的域名（`github.com` → `gh-proxy.com`）

认证头和代理由 `scripts/fetch/github_fetch_latest.js` 自动处理。

## 调试技巧

如果包安装失败，可以查看错误信息定位问题：

- **`no fetch_asset entry for os=xxx`**：`pkgs.json` 中缺少当前操作系统的 `arch` 配置
- **`no asset matching xxx`**：正则表达式没有匹配到任何 Release asset，检查资产名和正则
- **`GitHub API response missing tag_name`**：Release 没有 tag_name，检查仓库 Release 配置
- **`digest mismatch`**：下载的文件与 GitHub API 返回的 digest 不一致，可能是网络问题或文件被篡改。程序会自动删除坏文件并重新下载一次。

## 相关文档

- [SCRIPTS.zh_cn.md](SCRIPTS.zh_cn.md) — 脚本编写指南（获取源、六种钩子）
- [README.zh_cn.md](README.zh_cn.md) — 项目主页
