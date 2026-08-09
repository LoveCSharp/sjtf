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
-- Ensures the data directory exists.

local data_dir = install_dir .. "\\data"
local err = create_directory(data_dir)
if err then
    io.stderr:write("warning: failed to create data directory: " .. err .. "\n")
end
