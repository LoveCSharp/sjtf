# sjtf Scripting Guide

[English](SCRIPTS.md) | [中文](SCRIPTS.zh_cn.md)

`sjtf` supports one fetch-source script plus six hook scripts (before/after × install/upgrade/uninstall). Scripts are executed by the embedded [Jint](https://github.com/sebastienros/jint) engine with async/await enabled. All C# ↔ JS data exchange happens via JSON strings; scripts parse them with `JSON.parse(...)` and return values via `JSON.stringify(...)`.

## Script Types Overview

| Kind | Path | Triggered when | Purpose |
|---|---|---|---|
| Fetch source | `scripts/fetch/{fetch_source}_fetch_latest.js` | Install or upgrade | Resolve latest version, download URL, and digest |
| Before-install | `scripts/hooks/{name}-{os}-{arch}-before_install.js` | Before install's PlaceAsset | Pre-install cleanup (kill running processes, etc.) |
| After-install | `scripts/hooks/{name}-{os}-{arch}-after_install.js` | After install's PlaceAsset | Post-processing on first install |
| Before-upgrade | `scripts/hooks/{name}-{os}-{arch}-before_upgrade.js` | Before upgrade's PlaceAsset | Pre-upgrade cleanup |
| After-upgrade | `scripts/hooks/{name}-{os}-{arch}-after_upgrade.js` | After upgrade's PlaceAsset | Post-processing after upgrade |
| Before-uninstall | `scripts/hooks/{name}-{os}-{arch}-before_uninstall.js` | Before uninstall logic | Pre-uninstall cleanup |
| After-uninstall | `scripts/hooks/{name}-{os}-{arch}-after_uninstall.js` | Uninstall only | Cleanup after uninstallation |

Hooks are **auto-detected**: `sjtf` looks for the matching file in `scripts/hooks/` before/after each operation. The six hook kinds are independent — each one is silently skipped if its file is missing. No `pkgs.json` field is required.

## Directory Layout

```
scripts/
├── fetch/
│   ├── github_fetch_latest.js
│   └── update_code_visualstudio_com_fetch_latest.js
└── hooks/
    ├── sd-windows-x86_64-before_install.js
    ├── sd-windows-x86_64-after_install.js
    ├── sd-windows-x86_64-before_upgrade.js
    ├── sd-windows-x86_64-after_upgrade.js
    ├── sd-windows-x86_64-before_uninstall.js
    ├── sd-windows-x86_64-after_uninstall.js
    ├── vscode-windows-x86_64-after_install.js
    └── vscode-windows-x86_64-before_upgrade.js
```

## Globals Injected by C#

Before executing a script, C# binds the following global variables:

| Name | Type | Description |
|---|---|---|
| `pkgJSON` | string | Current package definition (raw JSON string) |
| `configJSON` | string | Configuration (raw JSON string from `config.toml`) |
| `os` | string | Current operating system (`windows` / `linux` / `macos`) |
| `arch` | string | Current architecture (`x86_64` / `aarch64` / `arm`) |
| `installDir` | string | Full path to the package install directory (hooks only) |
| `installRoot` | string | Global install root (`config.install_dir`, hooks only) |

Inside the script, parse the JSON with `JSON.parse(...)`:

```javascript
const pkg = JSON.parse(pkgJSON);
const config = JSON.parse(configJSON);
```

## C# Registered Functions

The following functions are registered on the global scope and can be called directly. Functions returning `string?` return `null` on success and an error message on failure; functions returning `string` return JSON.

| Name | Signature | Description |
|---|---|---|
| `log(msg)` | `(string) => void` | Print to stdout with `[label]` prefix |
| `logError(msg)` | `(string) => void` | Print to stderr |
| `httpGet(url)` | `(string) => Promise<string>` | Synchronous HTTP GET, returns body string. `User-Agent` is added automatically from `config.toml`. |
| `httpGetWithHeaders(url, headersJson)` | `(string, string) => Promise<string>` | HTTP GET with extra headers. `headersJson` is a JSON object string; each entry overrides the default request header. |
| `createDirectory(path)` | `(string) => string?` | Create a directory (recursive). Returns `null` on success, error message on failure. |
| `removeDirectory(path)` | `(string) => string?` | Recursively delete a directory. |
| `removeFile(path)` | `(string) => string?` | Delete a file. |
| `directoryList(path)` | `(string) => string` | JSON object describing the directory contents. |
| `fileExists(path)` | `(string) => boolean` | Check whether a file exists. |
| `directoryExists(path)` | `(string) => boolean` | Check whether a directory exists. |
| `writeFile(path, content)` | `(string, string) => void` | Write a UTF-8 (no BOM) text file. |

## Fetch Source Scripts

### Purpose

Dynamically fetch the latest version information for a package based on the `fetch_source` field in `pkgs.json`. Different sources have different API formats, so JavaScript scripts are used for extensibility.

### Script Location

```
scripts/
└── fetch/
    └── {fetch_source}_fetch_latest.js
```

For example, if `fetch_source` is `github`, the script path is `scripts/fetch/github_fetch_latest.js`.

### Required Return Value

The script must export an `async function fetch()` that returns a `JSON.stringify(...)`-ed object:

```javascript
return JSON.stringify({
    version: "v1.2.3",       // Upstream version string, compared against installed.json
    url: "https://...",      // Download URL
    digest: "abc123...",     // Digest value (optional, defaults to empty string)
    digest_algorithm: "sha256"  // Digest algorithm (optional, defaults to "sha256")
});
```

### Example: GitHub Releases

```javascript
// scripts/fetch/github_fetch_latest.js

async function fetch() {
    const pkg = JSON.parse(pkgJSON);
    const config = JSON.parse(configJSON);

    const repo = pkg.repo;
    if (!repo) throw new Error("pkg.repo is required");

    const archTable = pkg.fetch_asset && pkg.fetch_asset.arch;
    if (!archTable) throw new Error("pkg.fetch_asset.arch is required");

    const assetEntry = archTable[os] && archTable[os][arch];
    if (!assetEntry) throw new Error("no fetch_asset entry for os=" + os + " arch=" + arch);

    const assetRe = assetEntry.file;
    if (typeof assetRe !== "string") throw new Error("missing file regex");

    const githubConfig = (config && config.github) || {};
    const token = githubConfig.token_classic || "";
    const proxy = githubConfig.proxy || "";

    const apiUrl = "https://api.github.com/repos/" + repo + "/releases/latest";

    let headersJson = "{}";
    if (typeof token === "string" && token !== "" && token.startsWith("ghp_")) {
        headersJson = JSON.stringify({ Authorization: "token " + token });
    }

    const body = await httpGetWithHeaders(apiUrl, headersJson);
    const release = JSON.parse(body);

    const tag = release.tag_name;
    if (typeof tag !== "string" || tag === "") {
        throw new Error("GitHub API response missing tag_name");
    }

    const assets = release.assets;
    if (!Array.isArray(assets)) throw new Error("GitHub API response missing assets array");

    let matched = null;
    for (const asset of assets) {
        if (new RegExp(assetRe).test(asset.name)) {
            matched = asset;
            break;
        }
    }
    if (matched === null) throw new Error("no asset matching " + assetRe);

    let digest = matched.digest || "";
    let digestAlgorithm = "sha256";
    if (typeof digest === "string") {
        const colonPos = digest.indexOf(":");
        if (colonPos >= 0) {
            digestAlgorithm = digest.substring(0, colonPos);
            digest = digest.substring(colonPos + 1);
        }
    }

    let downloadUrl = matched.browser_download_url;
    if (typeof proxy === "string" && proxy !== "") {
        downloadUrl = proxy + "/" + downloadUrl;
    }

    return JSON.stringify({
        version: tag,
        url: downloadUrl,
        digest: digest,
        digest_algorithm: digestAlgorithm
    });
}
```

### Example: VS Code Update API

```javascript
// scripts/fetch/update_code_visualstudio_com_fetch_latest.js

async function fetch() {
    const pkg = JSON.parse(pkgJSON);

    const entry = pkg.fetch_asset.arch[os] && pkg.fetch_asset.arch[os][arch];
    if (!entry) throw new Error("no fetch_asset entry for os=" + os + " arch=" + arch);

    // The "stable_latest_info_url" points to the VS Code update metadata API.
    // The API returns JSON with the real download URL, productVersion, and sha256hash.
    const updateUrl = entry.stable_latest_info_url;
    if (typeof updateUrl !== "string") throw new Error("missing stable_latest_info_url");

    const info = JSON.parse(await httpGet(updateUrl));
    return JSON.stringify({
        version: info.productVersion,
        url: info.url,
        digest: info.sha256hash || "",
        digest_algorithm: "sha256",
        type: entry.type
    });
}
```

> Each fetch-source script reads the fields dictated by its protocol. The `github` source reads `assetEntry.file` (a JavaScript regex); the `update_code_visualstudio_com` source reads `assetEntry.stable_latest_info_url` (a URL).

### Adding a New Fetch Source

To support a new version source, create `scripts/fetch/{name}_fetch_latest.js` and set the package's `fetch_source` field in `pkgs.json` to `{name}`. No C# code changes are needed.

## Before-Install Hooks

### Purpose

Execute immediately before `PlaceAsset` runs during install. Use this hook for pre-install cleanup that the installer itself cannot do — killing running processes backed by the same shims, removing leftover files from a previous install attempt, or backing up user data before the directory is overwritten.

> The hook runs on every install call (including re-installs when `skipIfUptodate` is false), independent of whether an `after_install` hook also exists.

### Script Location

```
scripts/
└── hooks/
    └── {name}-{os}-{arch}-before_install.js
```

For example, before installing vscode on Windows x86_64:
`scripts/hooks/vscode-windows-x86_64-before_install.js`

The hook is auto-detected by path — no `pkgs.json` field is required.

### Required Function Signature

```javascript
async function beforeInstall() {
    // ...
}
```

### Globals

`pkgJSON`, `configJSON`, `os`, `arch`, `installDir`, `installRoot` (see [Globals Injected by C#](#globals-injected-by-c)).

### Example

```javascript
// scripts/hooks/vscode-windows-x86_64-before_install.js

async function beforeInstall() {
    const exe = installDir + "\\Code.exe";
    const exists = fileExists(exe);
    if (exists) {
        log("stopping any running vscode instance before install");
        // ... custom cleanup logic ...
    }
}
```

## After-Install Hooks

### Purpose

Execute after a package is installed. Handles post-installation tasks that the installer itself cannot complete, such as creating data directories or generating wrapper scripts.

> **Note:** For simple wrapper script creation, consider using the `shell_script` shim block in `pkgs.json` instead of an after-install hook. `shell_script` supports `{PKG_INSTALL_DIR}` and `{INSTALL_DIR}` placeholders and writes files as UTF-8 without BOM.

### Script Location

```
scripts/
└── hooks/
    └── {name}-{os}-{arch}-after_install.js
```

For example, after installing vscode on Windows x86_64:
`scripts/hooks/vscode-windows-x86_64-after_install.js`

The hook is auto-detected by path — no `pkgs.json` field is required.

### Required Function Signature

```javascript
async function afterInstall() {
    // ...
}
```

### Globals

`pkgJSON`, `configJSON`, `os`, `arch`, `installDir`, `installRoot` (see [Globals Injected by C#](#globals-injected-by-c)).

### Example

```javascript
// scripts/hooks/vscode-windows-x86_64-after_install.js

async function afterInstall() {
    const dataDir = installDir + "\\data";
    const err = createDirectory(dataDir);
    if (err) {
        logError("warning: failed to create data directory: " + err + "\n");
    }
}
```

## Before-Upgrade Hooks

### Purpose

Execute immediately before `PlaceAsset` runs during upgrade. Use this hook for upgrade-only cleanup that depends on the existing install — flushing in-memory state of the running binary, persisting user data that the next version will not preserve, or stopping daemons so the new binary can replace their files cleanly.

Runs **only** when the upgrade actually proceeds — i.e. the installed version differs from the resolved latest version. Never fires on a first-time install.

### Script Location

```
scripts/
└── hooks/
    └── {name}-{os}-{arch}-before_upgrade.js
```

For example, before upgrading vscode on Windows x86_64:
`scripts/hooks/vscode-windows-x86_64-before_upgrade.js`

The hook is auto-detected by path. If the file is missing the upgrade proceeds without it — no fallback to `before_install.js`, `after_install.js`, or any other hook, and no error.

### Required Function Signature

```javascript
async function beforeUpgrade() {
    // ...
}
```

### Globals

`pkgJSON`, `configJSON`, `os`, `arch`, `installDir`, `installRoot` (see [Globals Injected by C#](#globals-injected-by-c)).

### Example

```javascript
// scripts/hooks/vscode-windows-x86_64-before_upgrade.js

async function beforeUpgrade() {
    const lockFile = installDir + "\\cache\\.lock";
    const err = removeFile(lockFile);
    if (err) {
        logError("warning: failed to clear stale lock: " + err + "\n");
    }
}
```

## After-Upgrade Hooks

### Purpose

Execute after a package is upgraded (replaced with a newer version). Runs **only** when the upgrade actually proceeds — i.e. the installed version differs from the resolved latest version. Unlike the after-install hook, this never fires on a first-time install.

Use this hook for upgrade-only work such as cleaning stale caches left by the previous version, regenerating caches that depend on the new binary, or bumping config files.

### Script Location

```
scripts/
└── hooks/
    └── {name}-{os}-{arch}-after_upgrade.js
```

For example, after upgrading vscode on Windows x86_64:
`scripts/hooks/vscode-windows-x86_64-after_upgrade.js`

The hook is auto-detected by path. If the file is missing the upgrade proceeds without it — no fallback to `after_install.js` and no error.

### Required Function Signature

```javascript
async function afterUpgrade() {
    // ...
}
```

### Globals

`pkgJSON`, `configJSON`, `os`, `arch`, `installDir`, `installRoot` (see [Globals Injected by C#](#globals-injected-by-c)).

### Example

```javascript
// scripts/hooks/vscode-windows-x86_64-after_upgrade.js

async function afterUpgrade() {
    const staleCache = installDir + "\\cache\\old";
    const err = removeDirectory(staleCache);
    if (err) {
        logError("warning: failed to remove stale cache: " + err + "\n");
    }
}
```

## Before-Uninstall Hooks

### Purpose

Execute immediately before uninstall logic runs (before shims and the install directory are removed). Use this hook for cleanup that depends on the package still being installed — stopping running processes backed by the same shims, flushing in-memory state, or backing up user data before the directory is removed.

> The hook runs on every uninstall call (including re-uninstall attempts where the package is still recorded in `installed.json`), independent of whether an `after_uninstall` hook also exists.

### Script Location

```
scripts/
└── hooks/
    └── {name}-{os}-{arch}-before_uninstall.js
```

For example, before uninstalling vscode on Windows x86_64:
`scripts/hooks/vscode-windows-x86_64-before_uninstall.js`

The hook is auto-detected by path. If the file is missing the uninstall proceeds without it — no fallback to `before_install.js`, `after_install.js`, `before_upgrade.js`, `after_upgrade.js`, `after_uninstall.js`, or any other hook, and no error.

### Required Function Signature

```javascript
async function beforeUninstall() {
    // ...
}
```

### Globals

`pkgJSON`, `configJSON`, `os`, `arch`, `installDir`, `installRoot` (see [Globals Injected by C#](#globals-injected-by-c)).

> During uninstall, `installDir` still points to a real directory (it has not been deleted yet), so it can be used to clean up files inside it.

### Example

```javascript
// scripts/hooks/vscode-windows-x86_64-before_uninstall.js

async function beforeUninstall() {
    const exe = installDir + "\\Code.exe";
    const exists = fileExists(exe);
    if (exists) {
        log("stopping any running vscode instance before uninstall");
        // ... custom cleanup logic ...
    }
}
```

## After-Uninstall Hooks

### Purpose

Execute after a package is uninstalled. Used to clean up files created during installation, such as removing shims or residual data.

### Script Location

```
scripts/
└── hooks/
    └── {name}-{os}-{arch}-after_uninstall.js
```

For example, after uninstalling vscode on Windows x86_64:
`scripts/hooks/vscode-windows-x86_64-after_uninstall.js`

The hook is auto-detected by path — no `pkgs.json` field is required.

### Required Function Signature

```javascript
async function afterUninstall() {
    // ...
}
```

### Globals

`pkgJSON`, `configJSON`, `os`, `arch`, `installDir`, `installRoot` (see [Globals Injected by C#](#globals-injected-by-c)).

> During uninstall, `installDir` still points to a real directory (it has not been deleted yet), so it can be used to clean up files inside it.

### Example

```javascript
// scripts/hooks/vscode-windows-x86_64-after_uninstall.js

async function afterUninstall() {
    const symlinkDir = installRoot + "\\shims";
    const outputPath = symlinkDir + "\\code.cmd";

    const err = removeFile(outputPath);
    if (err) {
        logError("warning: " + err + "\n");
    }
}
```

## Notes

1. **Path matching**: Hook paths must strictly match `{name}-{os}-{arch}-{before|after}_{install|upgrade|uninstall}.js`, otherwise the hook is skipped silently.
2. **Independent hooks**: `before_install`, `before_upgrade`, `before_uninstall`, `after_install`, `after_upgrade`, and `after_uninstall` are six independent hooks. No operation ever falls back to a different hook — if a hook file is missing, that step is silently skipped.
3. **Error handling**: Hook failures do not abort install/upgrade/uninstall; errors are logged to stderr.
4. **`installDir` accessible during uninstall**: The directory has not been deleted yet, so it can be used to clean up files inside it.
5. **Shims directory auto-created**: `installRoot/shims` is created automatically when the program starts; no need to create it manually in hooks.
6. **Async functions**: All entry functions (`fetch`, `beforeInstall`, `beforeUpgrade`, `afterInstall`, `afterUpgrade`, `beforeUninstall`, `afterUninstall`) must be declared `async`; you can `await` the registered C# functions.
7. **No filesystem sandboxing**: Scripts run with full process privileges and can call any registered C# function. Do not run untrusted scripts.