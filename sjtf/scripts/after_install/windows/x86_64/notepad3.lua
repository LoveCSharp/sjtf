-- scripts/after_install/notepad3.lua
-- After-install script for Notepad3
-- Receives globals from C#:
--   pkg          - package definition table
--   config       - configuration table
--   os           - current OS string
--   arch         - current architecture string
--   install_dir  - full path to the package install directory
--   install_root - root install directory
--
-- Creates notepad3.cmd and np3.cmd in the symlink directory.

local symlink_dir = install_root .. "\\symlink"

local content = "@echo off\r\nsetlocal\r\n\"" .. install_dir .. "\\Notepad3.exe\" %*\r\n"

for _, name in ipairs({"notepad3.cmd", "np3.cmd"}) do
    local path = symlink_dir .. "\\" .. name
    local f = io.open(path, "w")
    if not f then
        error("cannot write " .. path)
    end
    f:write(content)
    f:close()
end
