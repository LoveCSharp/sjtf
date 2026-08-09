# sjtf Lua Scripting Guide

This document explains how to write three types of Lua scripts for sjtf: fetch source scripts, after-install scripts, and after-uninstall scripts.

## Script Types Overview

| Script Type | Path Template | Purpose |
|------------|---------------|---------|
| Fetch source | `scripts/{fetch_source}_fetch_latest.lua` | Resolve latest version, download URL, and digest from a source |
| After-install | `scripts/after_install/{os}/{arch}/{name}.lua` | Post-processing after package installation |
| After-uninstall | `scripts/after_uninstall/{os}/{arch}/{name}.lua` | Cleanup after package uninstallation |

## Fetch Source Scripts

### Purpose

Dynamically fetch the latest version information for a package based on the `fetch_source` field in `pkgs.json`. Different sources have different API formats, so Lua scripts are used for extensibility.

### Script Location

```
scripts/
└── {fetch_source}_fetch_latest.lua
```

For example, if `fetch_source` is `github`, the script path is `scripts/github_fetch_latest.lua`.

### Required Global Variable

The script must set the `result` global table:

```lua
result = {
    version = "v1.2.3",       -- Upstream version string, compared against installed.json
    url = "https://...",      -- Download URL
    digest = "abc123...",     -- Digest value (optional, defaults to empty string)
    digest_algorithm = "sha256"  -- Digest algorithm (optional, defaults to "sha256")
}
```

### Available Global Variables

C# injects the following global variables before executing the script:

| Variable | Type | Description |
|------|------|------|
| `pkg` | table | Current package definition (parsed from `pkgs.json`) |
| `config` | table | Configuration (parsed from `config.toml`) |
| `os` | string | Current operating system (`windows` / `linux` / `macos`) |
| `arch` | string | Current architecture (`x86_64` / `aarch64` / `arm`) |

### C# Registered Functions

| Function | Description |
|------|------|
| `http_get(url, headers_table?)` | Send HTTP GET request, return response body string. `User-Agent` is automatically added from `config.toml`. Keys in `headers_table` override default headers. |
| `json_decode(json_string)` | Parse JSON string into a Lua table |
| `regex_match(pattern, input)` | Regex match, returns boolean |

### Example

```lua
-- scripts/github_fetch_latest.lua

-- Fetch latest release from GitHub Releases API
local url = "https://api.github.com/repos/" .. pkg.repo .. "/releases/latest"
local body = http_get(url)
local info = json_decode(body)

-- Extract version
local version = info.tag_name
if version == nil then
    error("response missing tag_name")
end

-- Match asset
local asset_url = nil
for _, asset in ipairs(info.assets) do
    if regex_match(pkg.fetch_asset.arch[os][arch], asset.name) then
        asset_url = asset.browser_download_url
        break
    end
end

if asset_url == nil then
    error("no matching asset found")
end

-- Set result
result = {
    version = version,
    url = asset_url,
    digest = "",
    digest_algorithm = "sha256"
}
```

### Custom Fetch Sources

To support a new version source, simply create a `{fetch_source}_fetch_latest.lua` script and set the package's `fetch_source` field in `pkgs.json` to the corresponding prefix name. No C# code changes are needed.

## After-Install Scripts

### Purpose

Execute after a package is installed. Handles post-installation tasks that the installer itself cannot complete, such as creating wrapper scripts or data directories.

### Script Location

```
scripts/
└── after_install/
    └── {os}/
        └── {arch}/
            └── {name}.lua
```

For example, after installing vscode on Windows x86_64:
`scripts/after_install/windows/x86_64/vscode.lua`

### Available Global Variables

C# injects the following global variables before executing after-install scripts:

| Variable | Type | Description |
|------|------|------|
| `pkg` | table | Current package definition (parsed from `pkgs.json`) |
| `config` | table | Configuration (parsed from `config.toml`) |
| `os` | string | Current operating system |
| `arch` | string | Current architecture |
| `install_dir` | string | Full path to the package installation directory |
| `install_root` | string | Installation root directory (`install_dir` from `config.toml`) |

### C# Registered Functions

| Function | Description |
|------|------|
| `create_directory(path)` | Create a directory, returns `nil` on success or an error string on failure |

### Example

```lua
-- scripts/after_install/windows/x86_64/vscode.lua

-- Create data directory
local data_dir = install_dir .. "\\data"
local err = create_directory(data_dir)
if err then
    io.stderr:write("warning: failed to create data directory: " .. err .. "\n")
end
```

### Enabling in pkgs.json

```json
{
  "vscode": {
    "script_after_install": true
  }
}
```

## After-Uninstall Scripts

### Purpose

Execute after a package is uninstalled. Used to clean up files created during installation, such as removing shims or residual data.

### Script Location

```
scripts/
└── after_uninstall/
    └── {os}/
        └── {arch}/
            └── {name}.lua
```

For example, after uninstalling vscode on Windows x86_64:
`scripts/after_uninstall/windows/x86_64/vscode.lua`

### Available Global Variables

C# injects the following global variables before executing after-uninstall scripts:

| Variable | Type | Description |
|------|------|------|
| `pkg` | table | Current package definition (parsed from `pkgs.json`) |
| `config` | table | Configuration (parsed from `config.toml`) |
| `os` | string | Current operating system |
| `arch` | string | Current architecture |
| `install_dir` | string | Full path to the package installation directory (still accessible during uninstall, directory not yet deleted) |
| `install_root` | string | Installation root directory |

### C# Registered Functions

| Function | Description |
|------|------|
| `remove_file(path)` | Delete a file, returns `nil` on success or an error string on failure |

### Example

```lua
-- scripts/after_uninstall/windows/x86_64/vscode.lua

-- Remove wrapper script from shims directory
local symlink_dir = install_root .. "\\shims"
local output_path = symlink_dir .. "\\code.cmd"

local err = remove_file(output_path)
if err then
    io.stderr:write("warning: " .. err .. "\n")
end
```

### Enabling in pkgs.json

```json
{
  "vscode": {
    "script_after_uninstall": true
  }
}
```

## Notes

1. **Path matching**: Script paths must strictly match `{os}/{arch}/{name}.lua`, otherwise execution is skipped
2. **Error handling**: Script failures do not prevent the install/uninstall process from completing; errors are printed to stderr
3. **`install_dir` accessible during uninstall**: The directory has not been deleted yet, so it can be used to clean up files created within that directory
4. **Shims directory auto-created**: `install_root/shims` is automatically created when the program starts; no need to create it manually in scripts
5. **Standard Lua**: Standard Lua syntax and libraries can be used in scripts
