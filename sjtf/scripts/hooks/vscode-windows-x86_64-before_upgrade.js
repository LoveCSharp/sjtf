// scripts/hooks/vscode-windows-x86_64-before_upgrade.js
// Before-upgrade hook for VS Code.
// Lists the install directory and removes everything except the `data` folder,
// so PlaceAsset can lay down the new portable VS Code layout cleanly.
//
// Globals provided by C#:
//   pkgJSON    - JSON string
//   configJSON - JSON string
//   os, arch   - current platform identifiers
//   installDir - full path to the package install directory
//   installRoot - root install directory
//
// Functions exposed by C#:
//   directoryList(path)   -> JSON string {path, items:[{name,isDirectory,size}]}
//   removeDirectory(path) -> null on success, error string on failure
//   removeFile(path)      -> null on success, error string on failure

async function beforeUpgrade() {
    const listJSON = directoryList(installDir);
    const list = JSON.parse(listJSON);

    if (list.error) {
        logError("vscode before_upgrade: failed to list " + installDir + ": " + list.error + "\n");
        return;
    }

    log("vscode before_upgrade: cleaning " + installDir + " (keeping data/)");

    for (const item of list.items) {
        if (item.isDirectory && item.name === "data") {
            continue;
        }
        const fullPath = installDir + "\\" + item.name;
        if (item.isDirectory) {
            const err = removeDirectory(fullPath);
            if (err) {
                logError("vscode before_upgrade: failed to remove dir " + fullPath + ": " + err + "\n");
            } else {
                log("vscode before_upgrade: removed dir " + fullPath);
            }
        } else {
            const err = removeFile(fullPath);
            if (err) {
                logError("vscode before_upgrade: failed to remove file " + fullPath + ": " + err + "\n");
            } else {
                log("vscode before_upgrade: removed file " + fullPath);
            }
        }
    }
}