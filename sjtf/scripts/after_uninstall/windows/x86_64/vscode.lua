-- scripts/after_uninstall/vscode.lua
-- After-uninstall script for VS Code
-- Receives globals from C#:
--   pkg          - package definition table
--   config       - configuration table
--   os           - current OS string
--   arch         - current architecture string
--   install_dir  - full path to the package install directory
--   install_root - root install directory
--
-- C# registered functions:
--   remove_file(path) -> nil on success, error string on failure
--
-- Removes the code.cmd wrapper from the shims directory.

local symlink_dir = install_root .. "\\shims"
local output_path = symlink_dir .. "\\code.cmd"

local err = remove_file(output_path)
if err then
    io.stderr:write("warning: " .. err .. "\n")
end
