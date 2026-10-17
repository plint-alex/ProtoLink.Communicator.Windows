# Windows Communicator integration tests

`dotnet test` does not call `protolink.ru` unless you opt in. Live tests are skipped when `PROTOLINK_API_BASE` is unset. A host of `protolink.ru` or any subdomain also requires `PROTOLINK_ALLOW_PROD_TESTS=1`. The suite never copies `settings.json` `ApiBaseAddress` over that variable, so a Communicator profile pointed at production cannot redirect a run.

## Offline-only (no API)

These do not open a connection to an API. Run them with:

```powershell
dotnet test ProtoLink.Communicator.Windows.IntegrationTests --filter Category=Offline
```

| Class | What it checks |
| --- | --- |
| `SyncClassifierTests` | Local add / update / remove / rename classification |
| `SyncDirectionDecideTests` | Upload vs download vs skip from size and content hash |
| `CloudApiServiceContentLengthTests` | Chunked `getFile` responses with no Content-Length, using an in-memory handler |
| `LiveApiTargetTests` | The live-test gate: no default host, production blocked without opt-in |
| `NavigationHistoryTests` | AT-N6: push/pop order, cap 50, breadcrumb pushes |
| `NoteRemoteIdResolverTests` | AT-N5: mapped path → RemoteId; outside → null; SyncPathUtil case/separators |
| `EntityLinksTests` | AT-C4 / AT-X1: `{base}/{id:D}`, `?lang=` only when UI language set |
| `CloudFilePreviewTests` | AT-C2: extension → preview kind; local mirror pick |
| `CloudEntityRestoreTests` | AT-C7: restore decision + breadcrumb assembly |

Live ACL (needs API + password): `CloudFilesAclLiveTests` covers share AT-S30/S31 — private `getFile`/`getImage` 403 `no_access` for anon and partner, 200 for owner, unknown id 404 `not_found`, `Cache-Control: private` then `public, max-age=86400` after a public grant. Optional `PROTOLINK_TEST_PARTNER_PASSWORD` (defaults to `PROTOLINK_TEST_PASSWORD`).

`SeedContentHashesFromLocalTests` (`Category=LocalProfile`) also makes no HTTP call. It reads the real Communicator profile under `%LocalAppData%\ProtoLinkCommunicator` and may write `ContentHash` into that sync store. It is not part of the offline filter.

## Live tests against a local API

Start the API on port 5000, then:

```powershell
$env:PROTOLINK_API_BASE = "http://localhost:5000"
$env:PROTOLINK_TEST_PASSWORD = "<test account password>"   # login-based tests
dotnet test ProtoLink.Communicator.Windows.IntegrationTests --filter Category=Live
```

`PROTOLINK_TEST_LOGIN` overrides the default login (`alex.plint@gmail.com`). Optional: `PROTOLINK_TEST_CLOUD_FOLDER_ID`, `PROTOLINK_COMPARE_REL_PATH`, `PROTOLINK_TEST_HTTP_TIMEOUT_SEC`.

Login-based live tests (`Messenger*`, `CloudGetEntities*`, `CloudSyncRoundtrip*`, `CloudFileNameAfterUpload*`, `SyncEngineE2E*`, `CloudSyntheticTrashCleanup*`) need `PROTOLINK_TEST_PASSWORD`. Several others use the desktop app's `token.dat` and local notes folder (`SyncWalkDiagnostics*`, `SyncRootListing*`, `SyncFolderTiming*`, `FilmsOnlyUpload*`, `MyNotesFolderReconcile*`, `LocalNotesVsCloud*`, `CleanupMyNotesTrash*`).

Trash deletion stays behind a second switch: `PROTOLINK_CLEANUP_TRASH=1`.

## Production

```powershell
$env:PROTOLINK_API_BASE = "https://protolink.ru"
$env:PROTOLINK_ALLOW_PROD_TESTS = "1"
```

Only `1` opts in. Upload, reconcile, and trash-cleanup tests change the account they authenticate as. Leave the opt-in unset for a normal run.

`CheckFilmsCloud.csx` in this folder is not compiled by `dotnet test`. It is a scratch script with a hard-coded production URL.
