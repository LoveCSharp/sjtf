-- scripts/after_install/windows/x86_64/vscode.lua
-- After-install script for VS Code
-- Receives globals from C#:
--   pkg          - package definition table
--   config       - configuration table
--   os           - current OS string
--   arch         - current architecture string
--   install_dir  - full path to the package install directory
--   install_root - root install directory
--
-- C# registered functions:
--   create_directory(path) -> nil on success, error string on failure
--
-- Creates a code.cmd wrapper in the symlink directory and
-- ensures the data directory exists.

local code_cmd_path = install_dir .. "\\bin\\code.cmd"
local symlink_dir = install_root .. "\\symlink"
local output_path = symlink_dir .. "\\code.cmd"

local f = io.open(code_cmd_path, "r")
if not f then
    error("cannot read " .. code_cmd_path)
end

local content = f:read("*all")
f:close()

content = content:gsub("%%~dp0%.%.", install_dir)

local out = io.open(output_path, "w")
if not out then
    error("cannot write " .. output_path)
end

out:write(content)
out:close()

local data_dir = install_dir .. "\\data"
local err = create_directory(data_dir)
if err then
    io.stderr:write("warning: failed to create data directory: " .. err .. "\n")
end
