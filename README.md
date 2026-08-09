# sjtf - Command-Line Skeleton Tool

![.NET](https://img.shields.io/badge/.NET-10.0-blue)
![License: MIT](https://img.shields.io/badge/License-MIT-green)

A portable CLI package manager that downloads, verifies, and manages command-line tools from GitHub and other sources.

## Features

- 🚀 Install/uninstall/upgrade CLI tools with a single command
- 🔍 Lua script-based extensible version resolution
- ✅ SHA-256/SHA-1/SHA-512/MD5 digest verification
- 🔗 Automatic shim/symlink creation
- 🌐 Multi-platform: Windows, Linux, macOS
- 🏗️ Native AOT compilation support

## Quick Start

```bash
sjtf packages list    # List available packages
sjtf packages update  # Update pkgs.json from remote
sjtf list             # List installed packages
sjtf install fnm uv   # Install packages
sjtf uninstall fnm    # Uninstall a package
sjtf upgrade --all    # Upgrade all installed packages
sjtf favorites        # Sync with favorites.json
sjtf --version        # Show version
```

## Commands

| Command | Aliases | Description |
|---------|---------|-------------|
| `packages list` | `pkgs list`, `pkgs ls` | List packages defined in `pkgs.json` |
| `packages update` | `pkgs update`, `pkgs up` | Download latest `pkgs.json` from remote |
| `list` | `ls` | List installed packages |
| `install` | `i` | Install one or more packages |
| `uninstall` | `u`, `rm`, `remove` | Uninstall one or more packages |
| `upgrade` | `up` | Upgrade installed packages to latest |
| `favorites` | `favors` | Sync installed packages with `favorites.json` |
| `--version` | | Show version information |

## Configuration Files

All configuration files are located in the same directory as the executable.

> **Note (Windows):** Creating symbolic links requires Administrator privileges or Developer Mode enabled. Without it, symlink creation will fail.

### `config.toml`

Main configuration file. Automatically created with default values on first run.

```toml
[general]
install_dir = "D:\\sjtf_pkgs"     # Root directory for all installations

[pkgs]
remote_url = "https://cdn.jsdelivr.net/gh/LoveCSharp/sjtf@main/sjtf/pkgs.json"  # Remote pkgs.json URL for `sjtf packages update`

[download]
aria2_enable = true                # Enable aria2 download
max_connection_per_server = 10     # Max connections per server (1 ~ 16)
split = 10                         # Download split count (1 ~ 16)
min_split_size = 1                 # Minimum chunk size in MB (1 ~ 1024)

[aria2]
windows_x86_64 = "https://github.com/aria2/aria2/releases/download/release-1.37.0/aria2-1.37.0-win-64bit-build1.zip"

[github]
token_classic = "put your classic token here"  # GitHub personal access token (optional)
proxy = "https://gh-proxy.com"                 # GitHub proxy (optional)

[http.request.header]
user_agent = "Mozilla/5.0 (Windows NT 10.0; Win64, x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 Edg/151.0.0.0"
```

### `pkgs.json`

Package definitions. Use `sjtf packages update` to download the latest version from the remote URL configured in `config.toml`.

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
        "symlink": ["fnm.exe"]
      }
    },
    "fetch_source": "github"
  }
}
```

### `installed.json`

Auto-generated file tracking installed packages and their versions. Do not edit manually.

### `favorites.json`

JSON array of package names for the `favorites` command.

```json
[
  "fnm",
  "uv",
  "jq",
  "vscode"
]
```

## Supported Architectures

| OS | Architecture | Values |
|---|---|---|
| Windows | x64 | `windows` / `x86_64` |
| Windows | ARM64 | `windows` / `aarch64` |
| Linux | x64 | `linux` / `x86_64` |
| macOS | ARM64 | `macos` / `aarch64` |

## Digest Algorithms

| Algorithm | Identifier |
|---|---|
| SHA-256 | `sha256` |
| SHA-1 | `sha1` |
| SHA-512 | `sha512` |
| MD5 | `md5` |

## Extending with Lua Scripts

`sjtf` supports custom fetch sources and post-install/uninstall scripts via Lua.

- **Fetch sources**: `scripts/{fetch_source}_fetch_latest.lua`
- **Post-install scripts**: `scripts/after_install/{os}/{arch}/{name}.lua`
- **Post-uninstall scripts**: `scripts/after_uninstall/{os}/{arch}/{name}.lua`

See [Manual.md](Manual.md) for detailed documentation.

## Building

```bash
dotnet build
dotnet publish -c Release -r win-x64 --self-contained
```

## License

MIT — see [LICENSE](LICENSE) for details.
