-- scripts/update_code_visualstudio_com_fetch_latest.lua
-- Receives globals from C#:
--   pkg    - package definition table (parsed from pkgs.json)
--   config - configuration table (parsed from config.toml)
--   os     - current OS string (windows/linux/macos)
--   arch   - current architecture string (x86_64/aarch64/arm)
--
-- C# registered functions:
--   http_get(url, headers_table) -> response body string
--   json_decode(json_string) -> lua table
--
-- Sets global `result` to a table {version, url, digest} for C# to
-- wrap into DownloadPlan.

local fetch_asset = pkg.fetch_asset
if fetch_asset == nil then
    error("pkg.fetch_asset is required")
end

local arch_table = fetch_asset.arch
if arch_table == nil then
    error("fetch_asset.arch is required")
end

local os_entry = arch_table[os]
if os_entry == nil then
    error("no fetch_asset entry for os=" .. os)
end

local update_url = os_entry[arch]
if update_url == nil then
    error("no fetch_asset entry for os=" .. os .. " arch=" .. arch)
end

if type(update_url) ~= "string" then
    error("update URL must be a string")
end

local body = http_get(update_url)
local info = json_decode(body)

if info == nil then
    error("update API response is not valid JSON")
end

local version = info.productVersion
if version == nil or version == "" then
    error("update API response missing productVersion")
end

local download_url = info.url
if download_url == nil or download_url == "" then
    error("update API response missing url")
end

local digest = info.sha256hash or ""

result = {
    version = version,
    url = download_url,
    digest = digest,
    digest_algorithm = "sha256"
}
