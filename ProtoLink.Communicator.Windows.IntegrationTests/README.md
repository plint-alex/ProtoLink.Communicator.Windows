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
| `NotesViewModelCopyIdTests` | Notes tree Copy id: TryResolveRemoteId via INoteRemoteIdResolver (null when unsynced) |
| `CloudCopyIdTests` | Cloud Copy id: FormatEntityIdForClipboard Guid D + Copy id labels; virtual hubs flagged |
| `WindowsExplorerTests` | Notes Open in Explorer: `/select,"path"` args; missing path returns false |
| `EntityLinksTests` | AT-C4 / AT-X1: `{base}/{id:D}`, `?lang=` only when UI language set |
| `CloudFilePreviewTests` | AT-C2: extension → preview kind; local mirror pick |
| `CloudEntityRestoreTests` | AT-C7: restore decision + breadcrumb assembly |

Live ACL (needs API + password): `CloudFilesAclLiveTests` covers share AT-S30/S31 — private `getFile`/`getImage` 403 `no_access` for anon and partner, 200 for owner, unknown id 404 `not_found`, `Cache-Control: private` then `public, max-age=86400` after a public grant. Optional `PROTOLINK_TEST_PARTNER_PASSWORD` (defaults to `PROTOLINK_TEST_PASSWORD`).

Messenger-media feature ACL (AT-M11/12/22/23): `MessengerMediaFeatureAclLiveTests` creates a `CloudRoot/Chats/…` file with peer-read at create time (same shape as MediaSend), asserts B can read, C (`MessengerTestCredentials.DefaultPageLogin`) + anon get 403 `no_access` on getFile/getImage, and `Cache-Control: private`. Server AT-M30: `CloudFilesAddFileSizeLimitLiveTests`.

`SeedContentHashesFromLocalTests` (`Category=LocalProfile`) also makes no HTTP call. It reads the real Communicator profile under `%LocalAppData%\ProtoLinkCommunicator` and may write `ContentHash` into that sync store. It is not part of the offline filter.

## Live tests against a local API

Start the API on port 5000, then:

```powershell
$env:PROTOLINK_API_BASE = "http://localhost:5000"
$env:PROTOLINK_TEST_PASSWORD = "<test account password>"   # login-based tests
dotnet test ProtoLink.Communicator.Windows.IntegrationTests --filter Category=Live
```

### Test accounts (emails only)

See Agents `docs/ops/environment-map` § test accounts. Password is never stored in this repo — use User-scope `PROTOLINK_TEST_PASSWORD`.

| Role | Email | Constant / override |
| --- | --- | --- |
| Custom page (fixture primary) | `alex.plint@gmail.com` | `MessengerTestCredentials.PrimaryLogin` |
| Default page | `alex.plint@yandex.ru` | `MessengerTestCredentials.DefaultPageLogin` |
| Partner (AT stranger) | `molina.tina@ya.ru` | `MessengerTestCredentials.PartnerLogin` |

`PrimaryLogin` stays gmail unless a named test needs default-page (then use `DefaultPageLogin` or set `PROTOLINK_TEST_LOGIN`). Optional: `PROTOLINK_TEST_CLOUD_FOLDER_ID`, `PROTOLINK_COMPARE_REL_PATH`, `PROTOLINK_TEST_HTTP_TIMEOUT_SEC`.

Login-based live tests (`Messenger*`, `CloudGetEntities*`, `CloudSyncRoundtrip*`, `CloudFileNameAfterUpload*`, `SyncEngineE2E*`, `CloudSyntheticTrashCleanup*`, `SignalRCommandsHubLiveTests`) need `PROTOLINK_TEST_PASSWORD`. Several others use the desktop app's `token.dat` and local notes folder (`SyncWalkDiagnostics*`, `SyncRootListing*`, `SyncFolderTiming*`, `FilmsOnlyUpload*`, `MyNotesFolderReconcile*`, `LocalNotesVsCloud*`, `CleanupMyNotesTrash*`).

SignalR hub (LocalStand): `SignalRCommandsHubLiveTests` connects via app `SignalRService` (SignalR.Client 10.0.13) to `/hubs/commands`, asserts `message_sent` / `data_changed` / `message_read`, and Disconnect→Connect receive. Optional API process recycle: `$env:PROTOLINK_SIGNALR_API_RESTART=1` then filter `SignalRService_AutomaticReconnect_After_LocalStandApiRestart` (kills/restarts `ProtoLink.Api.exe` briefly).

Trash deletion stays behind a second switch: `PROTOLINK_CLEANUP_TRASH=1`.

## Production

```powershell
$env:PROTOLINK_API_BASE = "https://protolink.ru"
$env:PROTOLINK_ALLOW_PROD_TESTS = "1"
```

Only `1` opts in. Upload, reconcile, and trash-cleanup tests change the account they authenticate as. Leave the opt-in unset for a normal run.

`CheckFilmsCloud.csx` in this folder is not compiled by `dotnet test`. It is a scratch script with a hard-coded production URL.
