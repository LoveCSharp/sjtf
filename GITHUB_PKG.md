# Writing a GitHub Source Package

[English](GITHUB_PKG.md) | [中文](GITHUB_PKG.zh_cn.md)

This document provides a detailed guide on creating an sjtf package definition that fetches from GitHub Releases.

## Overview

A GitHub-based package requires two parts:

1. **Package definition in `pkgs.json`**: Describes the package's basic info, asset matching rules, install directory, and shim configuration
2. **`fetch_source` set to `github`**: Uses the built-in `scripts/fetch/github_fetch_latest.js` to automatically fetch the latest version from GitHub Releases API

`sjtf` has built-in support for GitHub Releases API, so no custom JavaScript scripts are needed.

## Complete Example

Here is a complete package definition (using `fnm` as an example):

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

A nested map keyed by OS and architecture. Each leaf value is an object carrying `file` and `type`, plus optional `installer` fields:

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

Structure: `fetch_asset.arch[os][arch]` is an object with the following fields:

| Field | Description |
|-------|-------------|
| `os` | `windows` / `linux` / `macos` — operating system |
| `arch` | `x86_64` / `aarch64` / `arm` — architecture |
| `file` | Asset URL (when the source returns a static download link) **or** a JavaScript regex string matched against the release asset's `name`. For non-URL sources, JavaScript `RegExp` syntax is used; the first matching asset is selected. |
| `type` | One of the package types below. |
| `install_params` | (optional, `installer` only) Arguments passed to the installer. Supports `{PKG_INSTALL_DIR}` and `{INSTALL_DIR}` placeholders. |
| `uninstall_program` | (optional, `installer` only) Uninstaller program file name, relative to the install directory. |
| `uninstall_params` | (optional, `installer` only) Arguments passed to the uninstaller. Supports `{PKG_INSTALL_DIR}` and `{INSTALL_DIR}` placeholders. |

#### `fetch_asset.type` (required)

Package type determining how the downloaded file is handled during installation:

| Type | Description |
|------|-------------|
| `portable-compressed-archive` | ZIP/TAR.GZ/7Z archive, automatically extracted to install directory |
| `portable-executable` | Standalone executable, copied directly to install directory |
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

#### `shim[os].symlink`

Key-value object mapping shim file names to target relative paths within the install directory.

```json
"symlink": {
  "fnm.exe": "fnm.exe",
  "jcode.exe": "jcode-windows-x86_64.exe"
}
```

This creates:
- `shims/fnm.exe` -> `langs\fnm\fnm.exe`
- `shims/jcode.exe` -> `ai\jcode\jcode-windows-x86_64.exe`

The **key** is the shim file name in `shims/`, the **value** is the relative path inside the package install directory.

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

Files are written as UTF-8 without BOM.

### `fetch_source` (required)

Specifies the version fetch source. For GitHub Releases, use the built-in `github` source:

```json
"fetch_source": "github"
```

Corresponding script: `scripts/fetch/github_fetch_latest.js`

This script:
1. Calls `https://api.github.com/repos/{repo}/releases/latest`
2. Parses `tag_name` as the version string
3. Uses `fetch_asset.arch[os][arch]` regex to match assets
4. Extracts `browser_download_url` as the download URL
5. Extracts `digest` for verification (GitHub API digest format: `sha256:abc123...`)

### Pre/post install, upgrade, and uninstall hooks

Hooks are **auto-detected** by path. Drop a JavaScript file at the expected location and `sjtf` will run it; no `pkgs.json` field is required. The six hook kinds are independent — each one is silently skipped if its file is missing.

- Before-install: `scripts/hooks/{name}-{os}-{arch}-before_install.js`
- After-install: `scripts/hooks/{name}-{os}-{arch}-after_install.js`
- Before-upgrade: `scripts/hooks/{name}-{os}-{arch}-before_upgrade.js`
- After-upgrade: `scripts/hooks/{name}-{os}-{arch}-after_upgrade.js`
- Before-uninstall: `scripts/hooks/{name}-{os}-{arch}-before_uninstall.js`
- After-uninstall: `scripts/hooks/{name}-{os}-{arch}-after_uninstall.js`

See [SCRIPTS.md](SCRIPTS.md) for the full hook authoring guide.

## Workflow

When a user runs `sjtf install fnm`:

1. **Version fetch**: Executes `scripts/fetch/github_fetch_latest.js`
   - Calls GitHub API for the latest Release
   - Matches asset regex, extracts download URL and version
   - Returns `DownloadPlan{Version, DownloadUrl, DigestAlgorithm, ExpectedDigest}`

2. **Download**: Multi-threaded chunked download (or aria2c) to cache directory
   - Cache filename format: `{name}-{os}-{arch}-{version}.{ext}`

3. **Verification**: Computes file digest and compares with GitHub API digest
   - Mismatch triggers immediate file deletion and one re-download
   - Second mismatch throws an error

4. **Install**: Handles based on `fetch_asset.arch.{os}.{arch}.type`
   - `portable-compressed-archive`: Extract to `pkg_install_relative_dir`
   - `portable-executable`: Copy to `pkg_install_relative_dir`
   - `installer`: Run installer with `install_params`

5. **Create shims**: Creates symlinks or shell scripts based on `shim[os]`

6. **After-install hook** (auto-detected): Executes `scripts/hooks/{name}-{os}-{arch}-after_install.js` if present

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

Auth headers and proxy are handled automatically by `scripts/fetch/github_fetch_latest.js`.

## Debugging

If package installation fails, check the error message:

- **`no fetch_asset entry for os=xxx`**: Missing `arch` config for current OS in `pkgs.json`
- **`no asset matching xxx`**: Regex didn't match any Release assets, check asset names and pattern
- **`GitHub API response missing tag_name`**: Release has no `tag_name`, check repository Release settings
- **`digest mismatch`**: Downloaded file doesn't match GitHub API digest, possible network issue or tampering. The corrupted file is deleted and re-downloaded automatically.

## Related Documentation

- [SCRIPTS.md](SCRIPTS.md) — Scripting Guide (fetch sources, six hook kinds)
- [README.md](README.md) — Project homepage
