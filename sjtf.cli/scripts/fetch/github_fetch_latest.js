// scripts/fetch/github_fetch_latest.js
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
//   log(msg)               - print to stdout
//   logError(msg)          - print to stderr
//   httpGet(url)           -> Promise<string>   - HTTP GET, returns body
//   httpGetWithHeaders(url, headersJSON) -> Promise<string>
//
// Exports an async `fetch()` that returns a JSON-stringified DownloadPlan
// (version / url / digest / digest_algorithm) to C#, plus the install-time
// metadata taken from fetch_asset.arch.{os}.{arch}:
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
    const config = JSON.parse(configJSON);

    const repo = pkg.repo;
    if (repo === undefined || repo === null) {
        throw new Error("pkg.repo is required");
    }

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

    const assetRe = assetEntry.file;
    if (typeof assetRe !== "string") {
        throw new Error("fetch_asset.arch." + os + "." + arch + ".file must be a string");
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

    if (typeof assetEntry.type !== "string" || assetEntry.type === "") {
        throw new Error("fetch_asset.arch." + os + "." + arch + ".type must be a non-empty string");
    }

    const githubConfig = (config && config.github) || {};
    const token = githubConfig.token_classic || "";
    const proxy = githubConfig.proxy || "";

    const apiUrl = "https://api.github.com/repos/" + repo + "/releases/latest";

    let headersJson = "{}";
    if (typeof token === "string" && token !== "" && token.startsWith("ghp_")) {
        headersJson = JSON.stringify({ Authorization: "token " + token });
    }

    const body = await httpGetWithHeaders(apiUrl, headersJson);
    const release = JSON.parse(body);

    if (release === null || typeof release !== "object") {
        throw new Error("GitHub API response is not valid JSON");
    }

    const tag = release.tag_name;
    if (typeof tag !== "string" || tag === "") {
        throw new Error("GitHub API response missing tag_name");
    }

    const assets = release.assets;
    if (!Array.isArray(assets)) {
        throw new Error("GitHub API response missing assets array");
    }

    let matched = null;
    for (const asset of assets) {
        if (new RegExp(assetRe, "i").test(asset.name)) {
            matched = asset;
            break;
        }
    }

    if (matched === null) {
        throw new Error("no asset matching " + assetRe);
    }

    let digest = matched.digest || "";
    let digestAlgorithm = "sha256";

    if (typeof digest === "string") {
        const colonPos = digest.indexOf(":");
        if (colonPos >= 0) {
            digestAlgorithm = digest.substring(0, colonPos);
            digest = digest.substring(colonPos + 1);
        }
    }

    let downloadUrl = matched.browser_download_url;
    if (typeof proxy === "string" && proxy !== "") {
        downloadUrl = proxy + "/" + downloadUrl;
    }

    return JSON.stringify({
        version: tag,
        url: downloadUrl,
        digest: digest,
        digest_algorithm: digestAlgorithm,
        type: assetEntry.type,
        install_program: installProgram,
        install_params: assetEntry.install_params || "",
        uninstall_program: uninstallProgram,
        uninstall_params: uninstallParams
    });
}