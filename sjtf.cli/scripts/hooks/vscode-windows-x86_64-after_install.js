// scripts/hooks/vscode-windows-x86_64-after_install.js
// After-install script for VS Code.
// Globals provided by C#:
//   pkgJSON    - JSON string
//   configJSON - JSON string
//   os, arch   - current platform identifiers
//   installDir - full path to the package install directory
//   installRoot - root install directory
//
// Functions exposed by C#:
//   createDirectory(path) -> null on success, error string on failure.
//
// Ensures the data directory exists.

async function afterInstall() {
    const dataDir = installDir + "\\data";
    const err = createDirectory(dataDir);
    if (err) {
        logError("warning: failed to create data directory: " + err + "\n");
    }
}