-- scripts/github_fetch_latest.lua
-- Receives globals from C#:
--   pkg    - package definition table (parsed from pkgs.json)
--   config - configuration table (parsed from config.toml)
--   os     - current OS string (windows/linux/macos)
--   arch   - current architecture string (x86_64/aarch64/arm)
--
-- C# registered functions:
--   http_get(url, headers_table) -> response body string
--   json_decode(json_string) -> lua table
--   regex_match(pattern, input) -> boolean
--
-- Sets global `result` to a table {version, url, digest} for C# to
-- wrap into DownloadPlan.

local repo = pkg.repo
if repo == nil then
    error("pkg.repo is required")
end

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

local asset_re = os_entry[arch]
if asset_re == nil then
    error("no fetch_asset entry for os=" .. os .. " arch=" .. arch)
end

local github_config = config.github or {}
local token = github_config.token_classic or ""
local proxy = github_config.proxy or ""

local api_url = "https://api.github.com/repos/" .. repo .. "/releases/latest"

local auth_headers = {}
if token ~= "" and token:sub(1, 4) == "ghp_" then
    auth_headers["Authorization"] = "token " .. token
end

local body = http_get(api_url, auth_headers)
local release = json_decode(body)

if release == nil then
    error("GitHub API response is not valid JSON")
end

local tag = release.tag_name
if type(tag) ~= "string" or tag == "" then
    error("GitHub API response missing tag_name")
end

local assets = release.assets
if type(assets) ~= "table" then
    error("GitHub API response missing assets array")
end

local matched = nil
for i, asset in ipairs(assets) do
    if regex_match(asset_re, asset.name) then
        matched = asset
        break
    end
end

if matched == nil then
    error("no asset matching " .. asset_re)
end

local digest = matched.digest or ""
local digest_algorithm = "sha256"

if type(digest) == "string" then
    local colon_pos = string.find(digest, ":")
    if colon_pos then
        digest_algorithm = string.sub(digest, 1, colon_pos - 1)
        digest = string.sub(digest, colon_pos + 1)
    end
end

result = {
    version = tag,
    url = matched.browser_download_url,
    digest = digest,
    digest_algorithm = digest_algorithm
}
