# sjtf - Command-Line Skeleton Tool

A portable CLI package manager that downloads, verifies, and manages command-line tools from GitHub and other sources.

## Quick Start

```bash
sjtf packages          # List available packages
sjtf list              # List installed packages
sjtf install fnm uv    # Install packages
sjtf uninstall fnm     # Uninstall a package
sjtf upgrade --all     # Upgrade all installed packages
sjtf favorites         # Sync with favorites.json
sjtf --version         # Show version
```

## Commands

### `packages` (alias: `pkgs`)

List all packages defined in `pkgs.json`.

```bash
sjtf packages
sjtf pkgs
```

### `list` (alias: `ls`)

List all installed packages with their versions.

```bash
sjtf list
sjtf ls
```

### `install` (alias: `i`)

Install one or more packages. If a package is already installed with the same version, it is skipped.

```bash
sjtf install fnm
sjtf install fnm uv jq
sjtf i vscode
```

### `uninstall` (alias: `u`, `rm`, `remove`)

Uninstall one or more packages. Removes symlinks, install directory, and runs uninstaller scripts if defined.

```bash
sjtf uninstall fnm
sjtf uninstall fnm uv jq
sjtf rm vscode
```

### `upgrade` (alias: `up`)

Upgrade one or more installed packages to the latest version. If the package is not installed, it is skipped.

```bash
sjtf upgrade fnm           # Upgrade a single package
sjtf upgrade fnm uv jq     # Upgrade multiple packages
sjtf upgrade --all         # Upgrade all installed packages
sjtf up --all
```

### `favorites` (alias: `favors`)

Sync installed packages with `favorites.json`. Packages in the list are installed/upgraded; packages not in the list are uninstalled.

```bash
sjtf favorites
sjtf favors
```

### `--version`

Show the current version of sjtf.

```bash
sjtf --version
```

## Configuration Files

All configuration files are located in the same directory as the executable.

### `config.toml`

Main configuration file. Automatically created with default values on first run.

```toml
[general]
install_dir = "D:\\sjtf_pkgs"     # Root directory for all installations
download_retry_max = 3             # Max download retry attempts
create_symlink = true              # Create symlinks (false to disable)

[github]
token_classic = "put your classic token here"  # GitHub personal access token (optional)
proxy = "https://gh-proxy.com"                 # GitHub proxy (optional)

[http.request.header]
user_agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 Edg/151.0.0.0"  # HTTP request User-Agent
```

### `pkgs.json`

Package definitions. Each package has:

| Field | Description |
|---|---|
| `repo` | GitHub repository (`owner/name`) |
| `fetch_asset` | Asset matching config (arch, type, pattern) |
| `install_dir` | Relative install directory |
| `symlinks` | Map of symlink name → relative path in install dir |
| `fetch_source` | Fetch source type (`github`, `update_code_visualstudio_com`, etc.) |
| `script_after_install` | `true` to run post-install Lua script |
| `script_after_uninstall` | `true` to run post-uninstall Lua script |

**Package types:**

| Type | Description |
|---|---|
| `portable-compressed-archive` | ZIP/TAR.GZ/7Z archive |
| `portable-exe` | Standalone executable |
| `installer` | Installer executable (runs with `install_params`) |

**Example:**
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

Auto-generated file tracking installed packages and their versions. Created automatically on first install/uninstall/upgrade/list command. Do not edit manually.

```json
{
  "fnm": "v1.39.0",
  "uv": "0.12.2"
}
```

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

## Custom Fetch Sources

### Overview

`sjtf` implements extensible version resolution through Lua scripts. Each fetch source corresponds to a Lua script file in `scripts/`, named `{fetch_source}_fetch_latest.lua`.

**Why Lua scripts?**

- Different sources have different API formats (GitHub Releases, VS Code update API, custom servers, etc.)
- Support new fetch sources without modifying C# code
- Customize authentication, proxy, version parsing, and more within scripts

### Integration with pkgs.json

In `pkgs.json`, each package specifies which fetch source to use via the `fetch_source` field:

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

At runtime:
1. Read the `fetch_source` value (e.g. `"github"`)
2. Construct script path: `scripts/{fetch_source}_fetch_latest.lua` (e.g. `scripts/github_fetch_latest.lua`)
3. Execute the Lua script, which must set a global `result` table containing:

| Field | Description |
|---|---|
| `version` | Upstream version string (compared against `installed.json`) |
| `url` | Resource download URL |
| `digest` | Digest value (optional, defaults to empty string) |
| `digest_algorithm` | Digest algorithm (optional, defaults to `"sha256"`) |

**Note:** `version`, `url`, and `digest` may come from different API responses or URLs. Version info may come from a metadata API, the download URL may need to be constructed based on the version, and the digest may require a separate request to the download URL.

### Global Variables Available to Lua Scripts

C# injects the following global variables before executing Lua scripts:

| Variable | Type | Description |
|---|---|---|
| `pkg` | table | Current package definition (parsed from `pkgs.json`) |
| `config` | table | Configuration (parsed from `config.toml`) |
| `os` | string | Current OS (`windows` / `linux` / `macos`) |
| `arch` | string | Current architecture (`x86_64` / `aarch64` / `arm`) |

### C# Registered Lua Functions

| Function | Description |
|---|---|
| `http_get(url, headers_table)` | Send HTTP GET request, return response body string. `User-Agent` is automatically added from `config.toml`. Keys in `headers_table` override defaults. |
| `json_decode(json_string)` | Parse JSON string into a Lua table |
| `regex_match(pattern, input)` | Regex match, returns boolean |

### Built-in Fetch Sources

| fetch_source | Script | Description |
|---|---|---|
| `github` | `scripts/github_fetch_latest.lua` | Calls GitHub Releases API, parses latest version and assets |
| `update_code_visualstudio_com` | `scripts/update_code_visualstudio_com_fetch_latest.lua` | Calls VS Code update API, parses latest version and download URL |

### Writing a Custom fetch_latest.lua

**Step 1:** Create `{fetch_source}_fetch_latest.lua` in `scripts/`

**Step 2:** The script must set the `result` global table

**Note:** `version`, `url`, and `digest` do not necessarily come from the same API response or URL. Version info may come from a metadata API, the download URL may need to be constructed based on the version, and the digest may require a separate request to the download URL.

**Example:** A custom fetch source where version info and download URL come from different APIs

```lua
-- scripts/my_source_fetch_latest.lua

-- Step 1: Get the latest version number
local meta_url = pkg.meta_url
local meta_body = http_get(meta_url)
local meta = json_decode(meta_body)
local version = meta.latest_version

-- Step 2: Construct download URL based on version
local url = string.format(
    "https://example.com/download/%s/%s/%s",
    pkg.name,
    version,
    pkg.filename_template
)

-- Step 3: Separately request the download URL to get the digest (e.g. ETag or SHA-256)
local head_body = http_get(url)
local head = json_decode(head_body)
local digest = head.sha256 or ""

-- Set result (required)
result = {
    version = version,
    url = url,
    digest = digest,
    digest_algorithm = "sha256"
}
```

**Corresponding pkgs.json:**

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

**Step 3:** In `pkgs.json`, set the package's `fetch_source` to the script prefix name (without `_fetch_latest.lua`)

### Notes

- When a script fails, the error message is displayed in the console (Lua errors are shown via exception chain unwrapping)
- Standard Lua syntax and libraries can be used in scripts
- If the script does not exist or does not set `result`, the installation will error and skip the package

## After Install / Uninstall Scripts

### Overview

`sjtf` supports executing custom Lua scripts after a package is installed or uninstalled. These scripts handle post-installation tasks that the installer itself cannot complete (e.g. creating wrapper scripts, cleaning up residual files).

### Integration with pkgs.json

In `pkgs.json`, two boolean fields control script execution:

| Field | Description |
|---|---|
| `script_after_install` | Set to `true` to execute `scripts/after_install/{os}/{arch}/{name}.lua` after installation |
| `script_after_uninstall` | Set to `true` to execute `scripts/after_uninstall/{os}/{arch}/{name}.lua` after uninstallation |

**Example:**
```json
{
  "vscode": {
    "fetch_source": "github",
    "script_after_install": true,
    "script_after_uninstall": true
  }
}
```

### Script Directory Structure

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

Where `{os}` is `windows`, `linux`, or `macos`, and `{arch}` is `x86_64`, `aarch64`, or `arm`.

### Available Global Variables

C# injects the following global variables before executing scripts:

| Variable | Type | Description |
|---|---|---|
| `pkg` | table | Current package definition (parsed from `pkgs.json`) |
| `config` | table | Configuration (parsed from `config.toml`) |
| `os` | string | Current operating system |
| `arch` | string | Current architecture |
| `install_dir` | string | Full path to the package installation directory |
| `install_root` | string | Installation root directory (the `install_dir` value from `config.toml`) |

### C# Registered Lua Functions

| Function | Description |
|---|---|
| `create_directory(path)` | Create a directory, returns `nil` on success or an error string on failure (available only in `after_install` scripts) |
| `remove_file(path)` | Delete a file, returns `nil` on success or an error string on failure (available only in `after_uninstall` scripts) |

### Writing Scripts

**After-install example:** Create a wrapper script for VS Code and ensure the data directory exists

```lua
-- scripts/after_install/windows/x86_64/vscode.lua

local code_cmd_path = install_dir .. "\\bin\\code.cmd"
local symlink_dir = install_root .. "\\symlink"
local output_path = symlink_dir .. "\\code.cmd"

-- Read the original script content
local f = io.open(code_cmd_path, "r")
if not f then error("cannot read " .. code_cmd_path) end
local content = f:read("*all")
f:close()

-- Replace relative paths with absolute paths
content = content:gsub("%%~dp0%.%.", install_dir)

-- Write to symlink directory
local out = io.open(output_path, "w")
if not out then error("cannot write " .. output_path) end
out:write(content)
out:close()

-- Create data directory
local data_dir = install_dir .. "\\data"
local err = create_directory(data_dir)
if err then
    io.stderr:write("warning: failed to create data directory: " .. err .. "\n")
end
```

**After-uninstall example:** Clean up the VS Code wrapper script

```lua
-- scripts/after_uninstall/windows/x86_64/vscode.lua

local symlink_dir = install_root .. "\\symlink"
local output_path = symlink_dir .. "\\code.cmd"

local err = remove_file(output_path)
if err then
    io.stderr:write("warning: " .. err .. "\n")
end
```

### Notes

- Script paths must strictly match `{os}/{arch}/{name}.lua`, otherwise execution is skipped
- Script failures do not prevent the install/uninstall process from completing (errors are printed to stderr)
- During uninstallation, `install_dir` is still accessible (the directory has not been deleted yet), which can be used to clean up files created within that directory
- The `install_root/symlink` directory is automatically created when the program starts; no need to create it manually in scripts

## Directory Structure

```
install_dir/
├── symlink/              # Symlinks and wrapper scripts
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

## Supported Architectures

| OS | Architecture | Value |
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
