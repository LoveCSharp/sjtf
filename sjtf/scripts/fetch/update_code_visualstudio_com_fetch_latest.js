// scripts/fetch/update_code_visualstudio_com_fetch_latest.js
// Globals provided by C#:
//   pkgJSON    - JSON string with package definition
//   configJSON - JSON string parsed from config.toml
//   os         - current OS string (windows/linux/macos)
//   arch       - current architecture string (x86_64/aarch64/arm)
//
// Functions exposed by C#:
//   httpGet(url) -> Promise<string> - HTTP GET, returns body
//
// Exports an async `fetch()` that returns a JSON-stringified DownloadPlan
// (version / url / digest / digest_algorithm) to C#.

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

    const updateUrl = assetEntry.file;
    if (typeof updateUrl !== "string") {
        throw new Error("fetch_asset.arch." + os + "." + arch + ".file must be a string");
    }

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
        digest_algorithm: "sha256"
    });
}