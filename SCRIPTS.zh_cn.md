# sjtf Lua 脚本编写指南

[English](SCRIPTS.md) | [中文](SCRIPTS.zh_cn.md)

本文档介绍如何为 sjtf 编写三种 Lua 脚本：获取源脚本、安装后脚本和卸载后脚本。

## 脚本类型概览

| 脚本类型 | 路径模板 | 作用 |
|---------|---------|------|
| 获取源脚本 | `scripts/{fetch_source}_fetch_latest.lua` | 从指定源解析最新版本、下载 URL 和摘要 |
| 安装后脚本 | `scripts/after_install/{os}/{arch}/{name}.lua` | 包安装完成后执行后续处理 |
| 卸载后脚本 | `scripts/after_uninstall/{os}/{arch}/{name}.lua` | 包卸载完成后执行清理工作 |

## 获取源脚本

### 作用

根据 `pkgs.json` 中 `fetch_source` 字段指定的源，动态获取包的最新版本信息。不同源的 API 格式不同，因此使用 Lua 脚本实现可扩展性。

### 脚本位置

```
scripts/
└── {fetch_source}_fetch_latest.lua
```

例如 `fetch_source` 为 `github` 时，脚本路径为 `scripts/github_fetch_latest.lua`。

### 必须设置的全局变量

脚本必须设置 `result` 全局表：

```lua
result = {
    version = "v1.2.3",       -- 上游版本字符串，用于与 installed.json 比较
    url = "https://...",      -- 下载 URL
    digest = "abc123...",     -- 摘要值（可选，默认空字符串）
    digest_algorithm = "sha256"  -- 摘要算法（可选，默认 "sha256"）
}
```

### 可用的全局变量

C# 在执行脚本前注入以下全局变量：

| 变量 | 类型 | 说明 |
|------|------|------|
| `pkg` | table | 当前包定义（从 `pkgs.json` 解析） |
| `config` | table | 配置（从 `config.toml` 解析） |
| `os` | string | 当前操作系统（`windows` / `linux` / `macos`） |
| `arch` | string | 当前架构（`x86_64` / `aarch64` / `arm`） |

### C# 注册的函数

| 函数 | 说明 |
|------|------|
| `http_get(url, headers_table?)` | 发送 HTTP GET 请求，返回响应体字符串。自动添加 `User-Agent` 头。`headers_table` 为可选参数，传入的键值对会覆盖默认请求头。 |
| `json_decode(json_string)` | 将 JSON 字符串解析为 Lua 表 |
| `regex_match(pattern, input)` | 正则匹配，返回布尔值 |

### 示例

```lua
-- scripts/github_fetch_latest.lua

-- 从 GitHub Releases API 获取最新版本
local url = "https://api.github.com/repos/" .. pkg.repo .. "/releases/latest"
local body = http_get(url)
local info = json_decode(body)

-- 提取版本号
local version = info.tag_name
if version == nil then
    error("response missing tag_name")
end

-- 匹配资产
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

-- 设置结果
result = {
    version = version,
    url = asset_url,
    digest = "",
    digest_algorithm = "sha256"
}
```

### 自定义获取源

如果要支持新的版本获取源，只需创建 `{fetch_source}_fetch_latest.lua` 脚本，然后在 `pkgs.json` 中将包的 `fetch_source` 字段设置为对应的前缀名称即可，无需修改 C# 代码。

## 安装后脚本

### 作用

包安装完成后执行。用于处理安装程序本身无法完成的后续任务，例如创建包装脚本、创建数据目录等。

### 脚本位置

```
scripts/
└── after_install/
    └── {os}/
        └── {arch}/
            └── {name}.lua
```

例如 Windows x86_64 上安装 vscode 后的脚本路径为：
`scripts/after_install/windows/x86_64/vscode.lua`

### 可用的全局变量

在安装后脚本中，C# 注入以下全局变量：

| 变量 | 类型 | 说明 |
|------|------|------|
| `pkg` | table | 当前包定义（从 `pkgs.json` 解析） |
| `config` | table | 配置（从 `config.toml` 解析） |
| `os` | string | 当前操作系统 |
| `arch` | string | 当前架构 |
| `install_dir` | string | 包的完整安装目录路径 |
| `install_root` | string | 安装根目录（`config.toml` 中的 `install_dir`） |

### C# 注册的函数

| 函数 | 说明 |
|------|------|
| `create_directory(path)` | 创建目录，成功返回 `nil`，失败返回错误字符串 |

### 示例

```lua
-- scripts/after_install/windows/x86_64/vscode.lua

-- 创建数据目录
local data_dir = install_dir .. "\\data"
local err = create_directory(data_dir)
if err then
    io.stderr:write("warning: failed to create data directory: " .. err .. "\n")
end
```

### 在 pkgs.json 中启用

```json
{
  "vscode": {
    "script_after_install": true
  }
}
```

## 卸载后脚本

### 作用

包卸载完成后执行。用于清理安装过程中创建的文件，例如删除 shims、清理残留数据等。

### 脚本位置

```
scripts/
└── after_uninstall/
    └── {os}/
        └── {arch}/
            └── {name}.lua
```

例如 Windows x86_64 上卸载 vscode 后的脚本路径为：
`scripts/after_uninstall/windows/x86_64/vscode.lua`

### 可用的全局变量

在卸载后脚本中，C# 注入以下全局变量：

| 变量 | 类型 | 说明 |
|------|------|------|
| `pkg` | table | 当前包定义（从 `pkgs.json` 解析） |
| `config` | table | 配置（从 `config.toml` 解析） |
| `os` | string | 当前操作系统 |
| `arch` | string | 当前架构 |
| `install_dir` | string | 包的完整安装目录路径（卸载时目录尚未删除，仍可访问） |
| `install_root` | string | 安装根目录 |

### C# 注册的函数

| 函数 | 说明 |
|------|------|
| `remove_file(path)` | 删除文件，成功返回 `nil`，失败返回错误字符串 |

### 示例

```lua
-- scripts/after_uninstall/windows/x86_64/vscode.lua

-- 删除 shims 目录中的包装脚本
local symlink_dir = install_root .. "\\shims"
local output_path = symlink_dir .. "\\code.cmd"

local err = remove_file(output_path)
if err then
    io.stderr:write("warning: " .. err .. "\n")
end
```

### 在 pkgs.json 中启用

```json
{
  "vscode": {
    "script_after_uninstall": true
  }
}
```

## 注意事项

1. **路径匹配**：脚本路径必须严格匹配 `{os}/{arch}/{name}.lua`，否则会被跳过执行
2. **错误处理**：脚本执行失败不会阻止安装/卸载流程完成，错误信息会打印到 stderr
3. **卸载时 install_dir 仍可访问**：卸载过程中，`install_dir` 指向的目录尚未被删除，可用于清理该目录内创建的文件
4. **shims 目录自动创建**：`install_root/shims` 目录会在程序启动时自动创建，脚本中无需手动创建
5. **Lua 标准库**：脚本中可以使用标准 Lua 语法和库
