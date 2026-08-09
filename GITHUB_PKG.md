# Writing a GitHub Source Package

[English](GITHUB_PKG.md) | [中文](GITHUB_PKG.zh_cn.md)

This document provides a detailed guide on creating an sjtf package definition that fetches from GitHub Releases.

## Overview

A GitHub-based package requires two parts:

1. **Package definition in `pkgs.json`**: Describes the package's basic info, asset matching rules, install directory, and shim configuration
2. **`fetch_source` set to `github`**: Uses the built-in `scripts/github_fetch_latest.lua` to automatically fetch the latest version from GitHub Releases API

`sjtf` has built-in support for GitHub Releases API, so no custom Lua scripts are needed.

## Complete Example

Here is a complete package definition (using `fnm` as an example):

```json
{
  "fnm": {
    "repo": "Schniz/fnm",
    "fetch_asset": {
      "arch": {
        "windows": {
          "x86_64": "(?=.*windows)(?=.*x86_64).*\\.zip$"
        }
      },
      "type": "portable-compressed-archive"
    },
    "pkg_install_relative_dir": "langs\\fnm",
    "shim": {
      "windows": {
        "symlink": ["fnm.exe"]
      }
    },
    "fetch_source": "github"
  }
}
```

## Field Reference

### `repo` (required)

GitHub repository identifier in `owner/name` format.

```json
"repo": "Schniz/fnm"
```

`sjtf` constructs the GitHub API URL:
```
https://api.github.com/repos/Schniz/fnm/releases/latest
```

### `fetch_asset` (required)

Asset matching configuration for selecting the correct file from a GitHub Release's assets list.

#### `fetch_asset.arch` (required)

A nested map of regular expressions keyed by OS and architecture.

```json
"arch": {
  "windows": {
    "x86_64": "(?=.*windows)(?=.*x86_64).*\\.zip$"
  },
  "linux": {
    "x86_64": "(?=.*linux)(?=.*x86_64).*\\.tar.gz$"
  },
  "macos": {
    "aarch64": "(?=.*macos)(?=.*aarch64).*\\.zip$"
  }
}
```

Structure: `fetch_asset.arch[os][arch] = regex_pattern`:

| Level | Values | Description |
|-------|--------|-------------|
| `os` | `windows` / `linux` / `macos` | Operating system |
| `arch` | `x86_64` / `aarch64` / `arm` | Architecture |
| value | Lua regex string | Matches asset file names |

Regular expressions use Lua (PCRE-style) syntax to match the `name` field of each asset in the release. The first matching asset is used.

#### `fetch_asset.type` (required)

Package type determining how the downloaded file is handled during installation:

| Type | Description |
|------|-------------|
| `portable-compressed-archive` | ZIP/TAR.GZ/7Z archive, automatically extracted to install directory |
| `portable-exe` | Standalone executable, copied directly to install directory |
| `installer` | Installer executable, runs with arguments specified by `install_params` |

### `pkg_install_relative_dir` (required)

The relative path within the installation root where the package will be installed. The final absolute path is `config.install_dir + pkg_install_relative_dir`.

```json
"pkg_install_relative_dir": "langs\\fnm"
```

If `config.toml` has `install_dir = "D:\\sjtf_pkgs"`, fnm's full install path will be:
```
D:\sjtf_pkgs\langs\fnm
```

### `shim` (optional)

Shim configuration, layered by operating system. Only takes effect when the current OS matches.

```json
"shim": {
  "windows": {
    "symlink": ["fnm.exe"],
    "shell_script": {
      "fnm.cmd": "@\"{PKG_INSTALL_DIR}\\fnm.exe\" %*",
      "fnm.ps1": "& \"{PKG_INSTALL_DIR}\\fnm.exe\" @args"
    }
  }
}
```

#### `shim[os].symlink`

String array creating symbolic links to files within the install directory. The link name is derived from the target file name.

```json
"symlink": ["fnm.exe"]
```

This creates `fnm.exe` -> `langs\fnm\fnm.exe` in the `shims/` directory.

#### `shim[os].shell_script`

Key-value object creating text shim files. Supports placeholders:

| Placeholder | Replaced with |
|-------------|---------------|
| `{PKG_INSTALL_DIR}` | Full package install path |
| `{INSTALL_DIR}` | Global install root (`config.install_dir`) |

```json
"shell_script": {
  "fnm.cmd": "@\"{PKG_INSTALL_DIR}\\fnm.exe\" %*",
  "fnm.ps1": "& \"{PKG_INSTALL_DIR}\\fnm.exe\" @args"
}
```

This creates:
- `shims/fnm.cmd`: Windows batch script
- `shims/fnm.ps1`: PowerShell script

### `fetch_source` (required)

Specifies the version fetch source. For GitHub Releases, use the built-in `github` source:

```json
"fetch_source": "github"
```

Corresponding script: `scripts/github_fetch_latest.lua`

This script:
1. Calls `https://api.github.com/repos/{repo}/releases/latest`
2. Parses `tag_name` as the version string
3. Uses `fetch_asset.arch[os][arch]` regex to match assets
4. Extracts `browser_download_url` as the download URL
5. Extracts `digest` for verification (GitHub API digest format: `sha256:abc123...`)

### `script_after_install` (optional)

Set to `true` to execute `scripts/after_install/{os}/{arch}/{name}.lua` after installation.

```json
"script_after_install": true
```

### `script_after_uninstall` (optional)

Set to `true` to execute `scripts/after_uninstall/{os}/{arch}/{name}.lua` after uninstallation.

```json
"script_after_uninstall": true
```

## Workflow

When a user runs `sjtf install fnm`:

1. **Version fetch**: Executes `scripts/github_fetch_latest.lua`
   - Calls GitHub API for the latest Release
   - Matches asset regex, extracts download URL and version
   - Returns `DownloadPlan{Version, DownloadUrl, DigestAlgorithm, ExpectedDigest}`

2. **Download**: Multi-threaded chunked download (or aria2c) to cache directory
   - Cache filename format: `{name}-{os}-{arch}-{version}.{ext}`

3. **Verification**: Computes file digest and compares with GitHub API digest
   - Mismatch triggers immediate file deletion and one re-download
   - Second mismatch throws an error

4. **Install**: Handles based on `fetch_asset.type`
   - `portable-compressed-archive`: Extract to `pkg_install_relative_dir`
   - `portable-exe`: Copy to `pkg_install_relative_dir`
   - `installer`: Run installer with `install_params`

5. **Create shims**: Creates symlinks or shell scripts based on `shim[os]`

6. **After-install script** (optional): Executes `after_install` Lua script

## Regex Examples

Write match patterns based on asset naming conventions:

| Asset Name | Match Regex | Description |
|-----------|-------------|-------------|
| `fnm-windows-x86_64.zip` | `(?=.*windows)(?=.*x86_64).*\.zip$` | Windows x64 zip |
| `uv-aarch64-apple-darwin.tar.gz` | `(?=.*aarch64)(?=.*darwin).*\.tar\.gz$` | macOS ARM64 |
| `jq-windows-amd64.exe` | `(?=.*windows)(?=.*amd64).*\.exe$` | Windows x64 exe |
| `rg-x86_64-unknown-linux-musl.tar.gz` | `(?=.*x86_64)(?=.*linux).*\.tar\.gz$` | Linux x64 |

Tips:
- Use `(?=.*keyword)` positive lookahead to ensure multiple keywords are present
- Use `\.` to escape dots when matching file extensions
- Use `$` anchor for end of string

## GitHub API Authentication

Configure GitHub authentication in `config.toml` to increase API rate limits:

```toml
[github]
token_classic = "ghp_xxxxxxxxxxxx"  # GitHub personal access token
proxy = "https://gh-proxy.com"       # Optional proxy
```

- `token_classic`: GitHub classic personal access token, must start with `ghp_`
- `proxy`: Proxy URL, replaces the domain in GitHub API requests (`github.com` -> `gh-proxy.com`)

Auth headers and proxy are handled automatically by `scripts/github_fetch_latest.lua`.

## Debugging

If package installation fails, check the error message:

- **`no fetch_asset entry for os=xxx`**: Missing `arch` config for current OS in `pkgs.json`
- **`no asset matching xxx`**: Regex didn't match any Release assets, check asset names and pattern
- **`GitHub API response missing tag_name`**: Release has no `tag_name`, check repository Release settings
- **`digest mismatch`**: Downloaded file doesn't match GitHub API digest, possible network issue or tampering

## Related Documentation

- [SCRIPTS.md](SCRIPTS.md) — Lua Scripting Guide (fetch sources, after-install, after-uninstall scripts)
- [README.md](README.md) — Project homepage
