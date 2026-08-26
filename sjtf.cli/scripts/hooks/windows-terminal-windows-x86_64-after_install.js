// scripts/hooks/windows-terminal-windows-x86_64-after_install.js
// After-install hook for Windows Terminal.
// Creates an empty `.portable` marker file in the install directory so that
// Windows Terminal uses the local data folder instead of %APPDATA%.
//
// Globals provided by C#:
//   installDir - full path to the package install directory
//   os, arch   - current platform identifiers
//
// Functions exposed by C#:
//   writeFile(path, content) -> void (UTF-8, no BOM)
//   log(msg)                 -> stdout with [label] prefix
//   logError(msg)            -> stderr

async function afterInstall() {
    const markerPath = installDir + "\\.portable";
    try {
        writeFile(markerPath, "");
        log("windows-terminal: created .portable marker at " + markerPath);
    } catch (ex) {
        logError("windows-terminal: failed to create .portable marker: " + ex + "\n");
    }
}