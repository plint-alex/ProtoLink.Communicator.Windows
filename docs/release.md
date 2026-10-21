# Communicator Windows release

## Version and git tag

- Source of truth: `<Version>` in `ProtoLink.Communicator.Windows/ProtoLink.Communicator.Windows.csproj`.
- After building the installer, the person cutting the release tags `v<version>` on the **exact commit that was built**.
- Deploy does **not** tag this repo.

## Build the installer

```powershell
.\tools\Build-Installer.ps1
```

Expected output:

- `artifacts/ProtoLink.Communicator.Windows-Setup-<version>.exe`
- Sibling `artifacts/ProtoLink.Communicator.Windows-Setup-<version>.exe.release.json` (`component`, `version`, `commit`, `buildDate`)

## Publish to cloud (`Publish-Release-To-Cloud.ps1`)

Uploads Windows and/or Android artifacts via the Files API and writes each uploaded platform’s version into `communicator-version-windows` / `communicator-version-android` (ids: `versionWindows` / `versionAndroid` in `ids.generated.json`, else scan release children by code). The release entity name slot is **not** overwritten with a version; optional `-Notes` still go to the release description. A missing version entity fails with “Run Apply-CommunicatorPage.ps1 first”. **Never run against production while testing.**

### Version history (D10)

When a platform’s version entity semver **changes**, the script first snapshots the current latest CloudFile into `communicator-release-history` (new CloudFile: history + platform parents, public read, versioned filename, MinIO object copy), then prunes that platform to at most **5** archive rows (lowest semver first; `DeleteEntity` also removes the MinIO object). Same-version re-upload skips archive creation. Idempotent: an existing archive for the same platform+semver is reused (blob re-copied). Latest stable CloudFile ids are unchanged after the snapshot (D3). Rules live in `tools/PublishReleaseArchive` (unit-tested).

### Required: `-BaseUrl`

There is **no default**. Omitting `-BaseUrl` fails with a clear error. Production must be explicit:

```powershell
-BaseUrl https://protolink.ru/
```

(HTTPS works for `protolink.ru`. Prefer `https://` over plain `http://`.)

Non-production examples:

```powershell
-BaseUrl http://localhost:5000/
```

### Optional: `-Version`

- When omitted, each artifact’s sibling `<artifact>.release.json` supplies `version` (platforms may differ).
- When given, it must match that artifact’s `release.json` (otherwise the script fails).
- If `release.json` is missing, `-Version` is required (transition until build scripts always write it).
- Android `versionCode` (and commit / buildDate when present) are written into the version entity’s description slot.

### Optional Windows / Android-only

- Default Windows path: newest `artifacts/ProtoLink.Communicator.Windows-Setup-*.exe` (excludes `*-stand*` LocalStand stubs and `*.release.json`).
- Default Android path: newest `artifacts/ProtoLink.Communicator.Android-*.apk` (same exclusions).
- Prefer explicit `-WindowsSetupPath` / `-AndroidApkPath` for real cuts (still the operator-safe path).
- `-SkipWindows` — Android-only publish.
- Omit or leave Android unset when only Windows should upload.
- Override with `-WindowsSetupPath` / `-AndroidApkPath`.

### Gate D (name + anonymous verify, default on)

After upload, the script (via `Publish-Release-Verify.ps1`):

1. Authenticated `GetEntities` on each uploaded CloudFile — name slot must equal the uploaded leaf (publish forces this after addFile/MinIO; API addFile alone keeps a prior display name).
2. `GET api/Entities/GetView/communicator` **without a token** — must be HTTP 200.
3. For each uploaded artifact, `GET api/Files/getFile/{id}/{fileName}` **without a token** — must be HTTP 200 and SHA256 must equal the local file.
4. Prints a paste-ready Status log line: platform, version, commit (from `release.json` when present), SHA256, download URL, base URL, time.

Any failure exits non-zero with a clear message. Use `-SkipVerify` only in emergencies (still prints a warning); do not treat a skipped run as a finished latest release.

### Examples

Stand / local (safe):

```powershell
.\tools\Publish-Release-To-Cloud.ps1 -BaseUrl http://localhost:5000/
```

Windows only, explicit paths:

```powershell
.\tools\Publish-Release-To-Cloud.ps1 `
  -BaseUrl http://localhost:5000/ `
  -WindowsSetupPath .\artifacts\ProtoLink.Communicator.Windows-Setup-1.0.7.exe
```

Android only:

```powershell
.\tools\Publish-Release-To-Cloud.ps1 `
  -BaseUrl http://localhost:5000/ `
  -SkipWindows `
  -AndroidApkPath .\artifacts\ProtoLink.Communicator.Android-1.0.3.apk
```

Emergency skip of anonymous verify (not for a normal release):

```powershell
.\tools\Publish-Release-To-Cloud.ps1 -BaseUrl http://localhost:5000/ -SkipVerify
```

Production (explicit HTTPS only):

```powershell
.\tools\Publish-Release-To-Cloud.ps1 -BaseUrl https://protolink.ru/
```

Via installer build + upload (also requires `-BaseUrl`):

```powershell
.\tools\Build-Installer.ps1 -Upload -BaseUrl http://localhost:5000/
```

Credentials and MinIO settings: Deploy notes (not copied here). CloudFile ids: `ids.generated.json` / `$env:PROTOLINK_VIEWEDITOR_IDS`.

## Related

- Agents release plan: `Agents/docs/release/index.html`
- Latest runbook: `Agents/docs/release/latest-runbook/index.html`
- Versioning decision: `Agents/docs/decisions/release-versioning-policy/index.html`
