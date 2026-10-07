# Shared notes editor

Canonical source (this folder in the Windows repo): `notes-editor.js`

Copied into:
- `ProtoLink.Communicator.Windows/NotesEditor/notes-editor.js` (embedded resource)
- `ProtoLink.Communicator.Android/app/src/main/assets/notes-editor.js`

After editing this file, copy to both platform paths before building. Keep workspace `../shared/notes-editor/` in sync if you use it for local Playwright runs outside the repo.

## Spec

See `docs/notes-editor-spec.md` (Windows repo) for behaviour + acceptance checklist.

## Tests

```bat
cd shared\notes-editor
npm install --no-save playwright@1.49.1
node run-tests.mjs
node wishlist-smoke.mjs
```

Win UI (app running, window restored/maximized): `ui-smoke.ps1`, `ui-notes-verify.ps1`.

C# shell embedding:

```bat
dotnet test ProtoLink.Communicator.Windows.IntegrationTests --filter NotesEditorShellTests
```
