// scripts/fetch/update_code_visualstudio_com_fetch_latest.js
// Globals provided by C#:
//   pkgJSON    - JSON string with package definition
//   configJSON - JSON string parsed from config.toml
//   os         - current OS string (windows/linux/macos)
//   arch       - current architecture string (x86_64/aarch64/arm)
//   installRoot - absolute install root directory (from config.toml)
//   installFull - absolute path to the package's future install directory
//                 (used to substitute {PKG_INSTALL_DIR} in uninstall_program)
//
// Functions exposed by C#:
//   httpGet(url) -> Promise<string> - HTTP GET, returns body
//
// Exports an async `fetch()` that returns a JSON-stringified DownloadPlan
// (version / url / digest / digest_algorithm) to C#, plus the install-time
// metadata taken from fetch_asset.arch.{os}.{arch}:
//   stable_latest_info_url
//                     - required, URL to the VS Code update metadata API
//                       (returns JSON containing the real download URL,
//                       productVersion and sha256hash)
//   type              - required, one of portable-compressed-archive /
//                       portable-executable / installer
//   install_program   - executable to run (default empty string);
//                       placeholder "{DOWNLOADED_CACHE_FILE_FULL_PATH}"
//                       is replaced by C# at install time with the cache file path;
//                       custom values are used verbatim
//   install_params    - optional, arguments passed to an installer
//   uninstall_program - absolute path to uninstaller (PKG_INSTALL_DIR already
//                       substituted by JS before return)
//   uninstall_params  - optional, arguments passed to the uninstaller

async function fetch() {
    const pkg = JSON.parse(pkgJSON);

    const fetchAsset = pkg.fetch_asset;
    if (fetchAsset === undefined || fetchAsset === null) {
        throw new Error("pkg.fetch_asset is required");
    }

    const archTable = fetchAsset.arch;
    if (archTable === undefined || archTable === null) {
        throw new Error("fetch_asset.arch is required");
    }

    const osEntry = archTable[os];
    if (osEntry === undefined || osEntry === null) {
        throw new Error("no fetch_asset entry for os=" + os);
    }

    const assetEntry = osEntry[arch];
    if (assetEntry === undefined || assetEntry === null) {
        throw new Error("no fetch_asset entry for os=" + os + " arch=" + arch);
    }

    const updateUrl = assetEntry.stable_latest_info_url;
    if (typeof updateUrl !== "string") {
        throw new Error("fetch_asset.arch." + os + "." + arch + ".stable_latest_info_url must be a string");
    }

    if (typeof assetEntry.type !== "string" || assetEntry.type === "") {
        throw new Error("fetch_asset.arch." + os + "." + arch + ".type must be a non-empty string");
    }

    const installProgram = (typeof assetEntry.install_program === "string" && assetEntry.install_program !== "")
        ? assetEntry.install_program
        : "";

    const uninstallProgramRaw = (typeof assetEntry.uninstall_program === "string")
        ? assetEntry.uninstall_program
        : "";

    const uninstallProgram = uninstallProgramRaw.includes("{PKG_INSTALL_DIR}")
        ? uninstallProgramRaw.replace("{PKG_INSTALL_DIR}", installFull)
        : uninstallProgramRaw;

    const uninstallParams = (typeof assetEntry.uninstall_params === "string")
        ? assetEntry.uninstall_params
        : "";

    const body = await httpGet(updateUrl);
    const info = JSON.parse(body);

    if (info === null || typeof info !== "object") {
        throw new Error("update API response is not valid JSON");
    }

    const version = info.productVersion;
    if (version === undefined || version === null || version === "") {
        throw new Error("update API response missing productVersion");
    }

    const downloadUrl = info.url;
    if (downloadUrl === undefined || downloadUrl === null || downloadUrl === "") {
        throw new Error("update API response missing url");
    }

    const digest = info.sha256hash || "";

    return JSON.stringify({
        version: version,
        url: downloadUrl,
        digest: digest,
        digest_algorithm: "sha256",
        type: assetEntry.type,
        install_program: "",  // vscode 不需要
        install_params: "",
        uninstall_program: "",
        uninstall_params: ""
    });
}