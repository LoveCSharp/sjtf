# sjtf 脚本编写指南

[English](SCRIPTS.en.md) | [中文](SCRIPTS.md)

`sjtf` 支持一种获取源脚本加上六种钩子脚本（前/后 × 安装/升级/卸载）。脚本由内嵌的 [Jint](https://github.com/sebastienros/jint) 引擎执行，开启了 async/await 支持。所有 C# ↔ JS 数据通过 JSON 字符串交换：脚本内部用 `JSON.parse(...)` 解析，用 `JSON.stringify(...)` 返回值。

## 脚本类型概览

| 类型 | 路径 | 触发时机 | 作用 |
|---|---|---|---|
| 获取源脚本 | `scripts/fetch/{fetch_source}_fetch_latest.js` | 安装或升级时 | 从指定源解析最新版本、下载 URL 和摘要 |
| 安装前钩子 | `scripts/hooks/{name}-{os}-{arch}-before_install.js` | 安装 PlaceAsset 之前 | 安装前的清理（如终止运行中的进程等） |
| 安装后钩子 | `scripts/hooks/{name}-{os}-{arch}-after_install.js` | 安装 PlaceAsset 之后 | 包首次安装完成后执行后续处理 |
| 升级前钩子 | `scripts/hooks/{name}-{os}-{arch}-before_upgrade.js` | 升级 PlaceAsset 之前 | 升级前的清理工作 |
| 升级后钩子 | `scripts/hooks/{name}-{os}-{arch}-after_upgrade.js` | 升级 PlaceAsset 之后 | 包升级完成后执行后续处理 |
| 卸载前钩子 | `scripts/hooks/{name}-{os}-{arch}-before_uninstall.js` | 卸载逻辑之前 | 卸载前的清理工作 |
| 卸载后钩子 | `scripts/hooks/{name}-{os}-{arch}-after_uninstall.js` | 仅卸载 | 包卸载完成后执行清理工作 |

钩子采用**自动检测**机制：每次操作前/完成后，`sjtf` 在 `scripts/hooks/` 中查找对应的脚本。六种钩子彼此独立——文件不存在时静默跳过。无需在 `pkgs.json` 中配置任何字段。

## 目录结构

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

## C# 注入的全局变量

执行脚本前，C# 会绑定以下全局变量：

| 名称 | 类型 | 说明 |
|---|---|---|
| `pkgJSON` | string | 当前包定义（原始 JSON 字符串） |
| `configJSON` | string | 配置（`config.toml` 的原始 JSON 字符串） |
| `os` | string | 当前操作系统（`windows` / `linux` / `macos`） |
| `arch` | string | 当前架构（`x86_64` / `aarch64` / `arm`） |
| `installDir` | string | 包的完整安装目录路径（仅钩子可用） |
| `installRoot` | string | 全局安装根目录，即 `config.install_dir`（仅钩子可用） |

脚本内部用 `JSON.parse(...)` 解析：

```javascript
const pkg = JSON.parse(pkgJSON);
const config = JSON.parse(configJSON);
```

## C# 注册的函数

以下函数挂载在全局作用域，可直接调用。返回 `string?` 的函数成功返回 `null`，失败返回错误信息；返回 `string` 的函数返回 JSON。

| 名称 | 签名 | 说明 |
|---|---|---|
| `log(msg)` | `(string) => void` | 带 `[label]` 前缀打印到 stdout |
| `logError(msg)` | `(string) => void` | 打印到 stderr |
| `httpGet(url)` | `(string) => Promise<string>` | 同步 HTTP GET，返回响应体字符串。自动从 `config.toml` 添加 `User-Agent`。 |
| `httpGetWithHeaders(url, headersJson)` | `(string, string) => Promise<string>` | 带额外请求头的 HTTP GET。`headersJson` 为 JSON 对象字符串，其中的键值对覆盖默认请求头。 |
| `createDirectory(path)` | `(string) => string?` | 创建目录（递归）。成功返回 `null`，失败返回错误信息。 |
| `removeDirectory(path)` | `(string) => string?` | 递归删除目录。 |
| `removeFile(path)` | `(string) => string?` | 删除文件。 |
| `directoryList(path)` | `(string) => string` | 描述目录内容的 JSON 对象。 |
| `fileExists(path)` | `(string) => boolean` | 检查文件是否存在。 |
| `directoryExists(path)` | `(string) => boolean` | 检查目录是否存在。 |
| `writeFile(path, content)` | `(string, string) => void` | 写入 UTF-8（无 BOM）文本文件。 |

## 获取源脚本

### 作用

根据 `pkgs.json` 中 `fetch_source` 字段指定的源，动态获取包的最新版本信息。不同源的 API 格式不同，因此使用 JavaScript 脚本实现可扩展性。

### 脚本位置

```
scripts/
└── fetch/
    └── {fetch_source}_fetch_latest.js
```

例如 `fetch_source` 为 `github` 时，脚本路径为 `scripts/fetch/github_fetch_latest.js`。

### 必须的返回值

脚本必须导出一个 `async function fetch()`，并 `return` 一个由 `JSON.stringify(...)` 包装的对象：

```javascript
return JSON.stringify({
    version: "v1.2.3",       // （必需）上游版本字符串，用于与 installed.json 比较
    url: "https://...",      // （必需）下载 URL
    type: "portable-compressed-archive",  // （必需）包类型，取自 fetch_asset.arch.{os}.{arch}.type
    digest: "abc123...",     // （可选，默认 ""）摘要值
    digest_algorithm: "sha256",  // （可选，默认 "sha256"）摘要算法
    install_program: "",     // （可选，默认 ""）安装程序可执行文件；占位符 {DOWNLOADED_CACHE_FILE_FULL_PATH} 由 C# 在安装时替换为缓存文件路径；自定义值按原值使用
    install_params: "",      // （可选，默认 ""）安装程序参数；支持 {PKG_INSTALL_DIR} 和 {INSTALL_DIR} 占位符
    uninstall_program: "",   // （可选，默认 ""）卸载程序可执行文件；{PKG_INSTALL_DIR} 必须由 JS 在返回前替换（使用 installFull 全局变量）—— C# 不会再做替换
    uninstall_params: ""     // （可选，默认 ""）卸载程序参数
});
```

字段说明：

- **必需**：`version`、`url`、`type`（`type` 一般直接从 `pkgs.json` 的 `fetch_asset.arch.{os}.{arch}.type` 透传）。
- **可选**：其余字段默认空字符串 `""`，`digest_algorithm` 默认 `"sha256"`。可选字段通常从 `pkgs.json` 的 `fetch_asset.arch.{os}.{arch}` 读取后透传。

| 字段 | 必需 | 默认值 | 说明 |
|---|---|---|---|
| `version` | 是 | — | 上游版本字符串，与 `installed.json` 比对 |
| `url` | 是 | — | 流水线要下载的 URL |
| `type` | 是 | — | 包类型，取自 `fetch_asset.arch.{os}.{arch}.type` |
| `digest` | 否 | `""` | 期望的十六进制摘要 |
| `digest_algorithm` | 否 | `"sha256"` | 摘要算法标识 |
| `install_program` | 否 | `""` | 安装程序可执行文件（占位符 `{DOWNLOADED_CACHE_FILE_FULL_PATH}` 由 C# 替换） |
| `install_params` | 否 | `""` | 安装程序参数（支持 `{PKG_INSTALL_DIR}` / `{INSTALL_DIR}`） |
| `uninstall_program` | 否 | `""` | 卸载程序可执行文件；`{PKG_INSTALL_DIR}` 必须由 JS 在返回前替换 |
| `uninstall_params` | 否 | `""` | 卸载程序参数 |

### 示例：GitHub Releases

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

    const installProgram = (typeof assetEntry.install_program === "string" && assetEntry.install_program !== "")
        ? assetEntry.install_program
        : "";

    const uninstallProgramRaw = (typeof assetEntry.uninstall_program === "string")
        ? assetEntry.uninstall_program
        : "";

    const uninstallProgram = uninstallProgramRaw.includes("{PKG_INSTALL_DIR}")
        ? uninstallProgramRaw.replace("{PKG_INSTALL_DIR}", installFull)
        : uninstallProgramRaw;

    const uninstallParams = (typeof assetEntry.uninstall_params === "string")
        ? assetEntry.uninstall_params
        : "";

    return JSON.stringify({
        version: tag,
        url: downloadUrl,
        digest: digest,
        digest_algorithm: digestAlgorithm,
        type: assetEntry.type,
        install_program: installProgram,
        install_params: assetEntry.install_params || "",
        uninstall_program: uninstallProgram,
        uninstall_params: uninstallParams
    });
}
```

### 示例：VS Code 更新 API

```javascript
// scripts/fetch/update_code_visualstudio_com_fetch_latest.js

async function fetch() {
    const pkg = JSON.parse(pkgJSON);

    const entry = pkg.fetch_asset.arch[os] && pkg.fetch_asset.arch[os][arch];
    if (!entry) throw new Error("no fetch_asset entry for os=" + os + " arch=" + arch);

    // "stable_latest_info_url" 指向 VS Code 更新元数据 API。
    // 该 API 返回包含真实下载 URL、productVersion 和 sha256hash 的 JSON。
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

> 每个获取源脚本读取的字段取决于其协议。`github` 源读取 `assetEntry.file`（JavaScript 正则）；`update_code_visualstudio_com` 源读取 `assetEntry.stable_latest_info_url`（URL）。

### 新增自定义获取源

要支持新的版本获取源，只需创建 `scripts/fetch/{name}_fetch_latest.js`，然后在 `pkgs.json` 中将包的 `fetch_source` 字段设置为 `{name}`，无需修改 C# 代码。

## 安装前钩子

### 作用

在安装 `PlaceAsset` 之前立即执行。用于安装程序本身无法完成的预清理：终止同一组 shim 指向的运行中进程、移除上一次安装尝试遗留的文件、或在被覆盖前备份用户数据。

> 钩子在每次安装调用时都会执行（包括 `skipIfUptodate` 为 false 时的重新安装），与是否存在 `after_install` 钩子无关。

### 脚本位置

```
scripts/
└── hooks/
    └── {name}-{os}-{arch}-before_install.js
```

例如 Windows x86_64 上安装 vscode 前的钩子路径为：
`scripts/hooks/vscode-windows-x86_64-before_install.js`

钩子按路径自动检测——无需在 `pkgs.json` 中启用任何字段。

### 函数签名

```javascript
async function beforeInstall() {
    // ...
}
```

### 可用全局变量

`pkgJSON`、`configJSON`、`os`、`arch`、`installDir`、`installRoot`（参见 [C# 注入的全局变量](#c-注入的全局变量)）。

### 示例

```javascript
// scripts/hooks/vscode-windows-x86_64-before_install.js

async function beforeInstall() {
    const exe = installDir + "\\Code.exe";
    const exists = fileExists(exe);
    if (exists) {
        log("安装前停止运行中的 vscode 实例");
        // ... 自定义清理逻辑 ...
    }
}
```

## 安装后钩子

### 作用

包首次安装完成后执行。用于处理安装程序本身无法完成的后续任务，例如创建数据目录或生成包装脚本。

> **注意：** 如果只是创建包装脚本，可以考虑使用 `pkgs.json` 中的 `shell_script` shim 块代替安装后钩子。`shell_script` 支持 `{PKG_INSTALL_DIR}` 和 `{INSTALL_DIR}` 占位符，并以 UTF-8 无 BOM 格式写入文件。

### 脚本位置

```
scripts/
└── hooks/
    └── {name}-{os}-{arch}-after_install.js
```

例如 Windows x86_64 上安装 vscode 后的钩子路径为：
`scripts/hooks/vscode-windows-x86_64-after_install.js`

钩子按路径自动检测——无需在 `pkgs.json` 中启用任何字段。

### 函数签名

```javascript
async function afterInstall() {
    // ...
}
```

### 可用全局变量

`pkgJSON`、`configJSON`、`os`、`arch`、`installDir`、`installRoot`（参见 [C# 注入的全局变量](#c-注入的全局变量)）。

### 示例

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

## 升级前钩子

### 作用

在升级 `PlaceAsset` 之前立即执行。用于依赖现有安装状态的预清理：将运行中二进制的状态刷出内存、保留下一版本不再携带的用户数据、或停守护进程以便新二进制干净地替换其文件。

**仅**在确实发生版本变更时触发——若解析的最新版本等于已安装版本则不会运行。也不会在首次安装时触发。

### 脚本位置

```
scripts/
└── hooks/
    └── {name}-{os}-{arch}-before_upgrade.js
```

例如 Windows x86_64 上升级 vscode 前的钩子路径为：
`scripts/hooks/vscode-windows-x86_64-before_upgrade.js`

钩子按路径自动检测。若文件缺失，升级流程正常进行——既不会回退到 `before_install.js`、`after_install.js` 或其他任何钩子，也不会报错。

### 函数签名

```javascript
async function beforeUpgrade() {
    // ...
}
```

### 可用全局变量

`pkgJSON`、`configJSON`、`os`、`arch`、`installDir`、`installRoot`（参见 [C# 注入的全局变量](#c-注入的全局变量)）。

### 示例

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

## 升级后钩子

### 作用

包升级（替换为新版本）完成后执行，**仅**在确实发生版本变更时触发——若解析的最新版本等于已安装版本则不会运行。也不会在首次安装时触发。

用于处理仅与升级相关的任务：清理旧版本遗留的缓存、为新二进制重新生成缓存、迁移配置文件等。

### 脚本位置

```
scripts/
└── hooks/
    └── {name}-{os}-{arch}-after_upgrade.js
```

例如 Windows x86_64 上升级 vscode 后的钩子路径为：
`scripts/hooks/vscode-windows-x86_64-after_upgrade.js`

钩子按路径自动检测。若文件缺失，升级流程正常进行——既不会回退到 `after_install.js`，也不会报错。

### 函数签名

```javascript
async function afterUpgrade() {
    // ...
}
```

### 可用全局变量

`pkgJSON`、`configJSON`、`os`、`arch`、`installDir`、`installRoot`（参见 [C# 注入的全局变量](#c-注入的全局变量)）。

### 示例

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

## 卸载前钩子

### 作用

在卸载逻辑（删除 shim 与安装目录）之前立即执行。用于依赖包仍处于已安装状态的清理：终止同一组 shim 指向的运行中进程、刷出内存中的运行状态、或在被删除前备份用户数据。

> 钩子在每次卸载调用时都会执行（包括 `installed.json` 中仍记录包名的重复卸载尝试），与是否存在 `after_uninstall` 钩子无关。

### 脚本位置

```
scripts/
└── hooks/
    └── {name}-{os}-{arch}-before_uninstall.js
```

例如 Windows x86_64 上卸载 vscode 前的钩子路径为：
`scripts/hooks/vscode-windows-x86_64-before_uninstall.js`

钩子按路径自动检测。若文件缺失，卸载流程正常进行——既不会回退到 `before_install.js`、`after_install.js`、`before_upgrade.js`、`after_upgrade.js`、`after_uninstall.js` 或其他任何钩子，也不会报错。

### 函数签名

```javascript
async function beforeUninstall() {
    // ...
}
```

### 可用全局变量

`pkgJSON`、`configJSON`、`os`、`arch`、`installDir`、`installRoot`（参见 [C# 注入的全局变量](#c-注入的全局变量)）。

> 卸载时 `installDir` 仍指向真实目录（尚未删除），可用于清理该目录内的文件。

### 示例

```javascript
// scripts/hooks/vscode-windows-x86_64-before_uninstall.js

async function beforeUninstall() {
    const exe = installDir + "\\Code.exe";
    const exists = fileExists(exe);
    if (exists) {
        log("卸载前停止运行中的 vscode 实例");
        // ... 自定义清理逻辑 ...
    }
}
```

## 卸载后钩子

### 作用

包卸载完成后执行。用于清理安装过程中创建的文件，例如删除 shim 包装脚本或残留数据。

### 脚本位置

```
scripts/
└── hooks/
    └── {name}-{os}-{arch}-after_uninstall.js
```

例如 Windows x86_64 上卸载 vscode 后的钩子路径为：
`scripts/hooks/vscode-windows-x86_64-after_uninstall.js`

钩子按路径自动检测——无需在 `pkgs.json` 中启用任何字段。

### 函数签名

```javascript
async function afterUninstall() {
    // ...
}
```

### 可用全局变量

`pkgJSON`、`configJSON`、`os`、`arch`、`installDir`、`installRoot`（参见 [C# 注入的全局变量](#c-注入的全局变量)）。

> 卸载时 `installDir` 仍指向真实目录（尚未删除），可用于清理该目录内的文件。

### 示例

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

## 注意事项

1. **路径匹配**：钩子路径必须严格匹配 `{name}-{os}-{arch}-{before|after}_{install|upgrade|uninstall}.js`，否则会被静默跳过。
2. **六种钩子彼此独立**：`before_install`、`before_upgrade`、`before_uninstall`、`after_install`、`after_upgrade`、`after_uninstall` 是六种互相独立的钩子。任意操作都不会回退到其他钩子——任一文件缺失时该步骤静默跳过。
3. **错误处理**：钩子执行失败不会终止安装/升级/卸载流程，错误信息会打印到 stderr。
4. **卸载时 `installDir` 仍可访问**：卸载过程中目录尚未被删除，可用于清理该目录内创建的文件。
5. **shims 目录自动创建**：`installRoot/shims` 目录会在程序启动时自动创建，脚本中无需手动创建。
6. **异步函数**：所有入口函数（`fetch`、`beforeInstall`、`beforeUpgrade`、`afterInstall`、`afterUpgrade`、`beforeUninstall`、`afterUninstall`）必须声明为 `async`，可以直接 `await` 注册的 C# 函数。
7. **无沙箱保护**：脚本以当前进程权限运行，可调用任何已注册的 C# 函数，请勿执行不受信任的脚本。