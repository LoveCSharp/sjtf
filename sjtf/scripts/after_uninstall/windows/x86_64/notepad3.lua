-- scripts/after_uninstall/notepad3.lua
-- After-uninstall script for Notepad3
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
-- Removes notepad3.cmd and np3.cmd from the symlink directory.

local symlink_dir = install_root .. "\\symlink"

for _, name in ipairs({"notepad3.cmd", "np3.cmd"}) do
    local path = symlink_dir .. "\\" .. name
    local err = remove_file(path)
    if err then
        io.stderr:write("warning: " .. err .. "\n")
    end
end
