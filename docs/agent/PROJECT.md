# ProtoLink.Communicator.Windows

> Живая документация для Cursor-агента.

## Назначение

Windows-клиент ProtoLink Communicator (Messenger, Notes, Cloud) с тем же контрактом sync, что Android.

## Структура

- `ProtoLink.Communicator.Windows/` — основное приложение
- `ProtoLink.Communicator.Windows.IntegrationTests/`
- `tools/CloudBlobUpload/`
- Solution: `ProtoLink.Communicator.Windows.sln`
- `docs/notes-and-cloud-sync.md` — канон sync
- `shared/notes-editor/` — канон JS-редактора + Playwright/UI smoke
- `ProtoLink.Communicator.Windows/NotesEditor/notes-editor.js` — embedded копия для WebView2

## Как запускать / проверять

```text
dotnet build ProtoLink.Communicator.Windows.sln
dotnet test ProtoLink.Communicator.Windows.IntegrationTests --filter NotesEditorShellTests
cd shared\notes-editor && node run-tests.mjs
```

Publish / install:

```text
tools\Publish.ps1
# затем копия artifacts\publish → "C:\Program Files\ProtoLink Communicator\" (admin)
```

## Ключевые решения

- Notes ≈ offline FS; Cloud владеет reconcile FS ↔ meta ↔ server.
- Full reconcile: start/login, map folder, SignalR `data_changed`, manual Sync.
- Local-only push: таймер 15s + save note; пустая meta → upgrade to full.
- После успешного local push с upload — SignalR `data_changed`; full sync сам `data_changed` не шлёт.
- Один sync за раз; busy + `data_changed` → coalesce один full после.
- Порядок дерева Notes: сейчас A–Z по имени папки (`NotesFileSystemService`); отдельного order/sidecar нет (см. обсуждение `_order.json` для будущего reorder+sync).

## Грабли и запреты

- Не делать Notes cloud-aware в UI.
- Не триггерить sync filesystem watchers / window Activate как full sync (см. док).
- Меняя sync — синхронизировать Android `:sync` / поведение.
- WebView2 (`NotesWebView.CoreWebView2`) — только с UI-диспетчера. Disk IO (`NotesFileService`) использует `ConfigureAwait(false)`; после await нельзя трогать WebView напрямую. Паттерн: `RunOnUiAsync` / `NotifyMappedSyncCompleted`. `_onSyncCompleted` всегда через dispatcher.
- `CoreWebView2Environment.CreateAsync` / `EnsureCoreWebView2Async` — только на STA (UI). Нельзя после `ConfigureAwait(false)` → `RPC_E_CHANGED_MODE` (0x80010106). InitWebView остаётся на dispatcher.
- Notes editor: правки только в `shared/notes-editor/notes-editor.js`, потом копия в `NotesEditor/` и Android `assets/`. Кнопки панели `Focusable=False` + `AutomationProperties.Name`. Enter в середине чеклиста — split текста на новый пункт.

## Связанные доки

- `docs/notes-and-cloud-sync.md` — полная спека
- `docs/notes-editor-spec.md` — ТЗ редактора + acceptance checklist
- `shared/notes-editor/README.md` — shared editor shell + тесты

## Открытые вопросы / TODO

- Стабильный/пользовательский порядок заметок + sync (`_order.json` sidecar) — не реализовано.

## История (кратко)

- 2026-10-07 — notes editor: Enter mid-item splits text into new checkbox; Ctrl+1; multi-line → checklist; contenteditable checkbox click; AutomationProperties on toolbar; vend `shared/notes-editor` + `run-tests.mjs` (15) / `wishlist-smoke.mjs`
- 2026-10-07 — notes editor ТЗ + per-line ☐ toggle; WebView2 STA / selectionState JsonDocument fix
- 2026-10-06 — notes editor: shared JS (paste sanitize, checklist keys, shortcuts); toolbar active state + indent
- 2026-10-06 — fix unobserved InvalidOperationException: reload open note after sync / index change only on UI thread
- 2026-09-26 — создан каркас agent-дока
