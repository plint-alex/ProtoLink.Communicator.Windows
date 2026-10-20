using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Windows.Input;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Messenger;
using ProtoLink.Communicator.Windows.Utilities;

namespace ProtoLink.Communicator.Windows.ViewModels;

public partial class MessengerViewModel
{
    private readonly CloudApiService _cloudApi;
    private readonly MessengerChatFolderResolver? _chatFolders;
    private readonly Dictionary<Guid, SemaphoreSlim> _uploadGates = new();
    private string? _attachStatusMessage;

    public ObservableCollection<PendingAttachmentDraft> PendingAttachments { get; } = new();

    public string? AttachStatusMessage
    {
        get => _attachStatusMessage;
        private set
        {
            if (_attachStatusMessage == value) return;
            _attachStatusMessage = value;
            OnPropertyChanged();
        }
    }

    public ICommand AttachFromDiskCommand { get; private set; } = null!;
    public ICommand ChooseFromCloudCommand { get; private set; } = null!;
    public ICommand RemovePendingAttachmentCommand { get; private set; } = null!;
    public ICommand RetryFailedSendCommand { get; private set; } = null!;
    public ICommand CancelFailedSendCommand { get; private set; } = null!;

    private void InitMediaSendCommands()
    {
        AttachFromDiskCommand = new RelayCommand(
            _ => AttachFromDisk(),
            _ => SelectedContact != null);
        ChooseFromCloudCommand = new RelayCommand(
            _ => _ = ChooseFromCloudAsync(),
            _ => SelectedContact != null && _authService.CurrentToken != null);
        RemovePendingAttachmentCommand = new RelayCommand(
            p => RemovePendingAttachment(p as PendingAttachmentDraft),
            p => p is PendingAttachmentDraft);
        RetryFailedSendCommand = new RelayCommand(
            async p => await RetryFailedSendAsync(p as MessageViewModel),
            p => p is MessageViewModel m && m.ShowFailedActions);
        CancelFailedSendCommand = new RelayCommand(
            async p => await CancelFailedSendAsync(p as MessageViewModel),
            p => p is MessageViewModel m && m.ShowFailedActions);

        PendingAttachments.CollectionChanged += (_, _) => CommandManager.InvalidateRequerySuggested();
    }

    private bool CanSendNow() =>
        SelectedContact != null
        && (!string.IsNullOrWhiteSpace(MessageText) || PendingAttachments.Count > 0);

    private void AttachFromDisk()
    {
        AttachStatusMessage = null;
        if (SelectedContact == null) return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Attach file",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true) return;

        foreach (var path in dlg.FileNames)
            TryAddPendingAttachment(path);
    }

    private async Task ChooseFromCloudAsync()
    {
        AttachStatusMessage = null;
        if (SelectedContact == null || _authService.CurrentToken == null) return;

        var myUserId = _authService.CurrentToken.UserId;
        var partnerId = SelectedContact.Id;
        var pickerVm = new CloudPickerViewModel(_cloudApi, myUserId, partnerId);
        var window = new Views.CloudPickerWindow(pickerVm)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        await pickerVm.OpenAtRootAsync().ConfigureAwait(true);
        if (window.ShowDialog() != true || window.Selection is not { } sel)
            return;

        TryAddCloudPick(sel);
    }

    /// <summary>M3 — queue an existing CloudFile by id (no local path, no copy).</summary>
    public bool TryAddCloudPick(CloudPickerSelection selection)
    {
        AttachStatusMessage = null;
        const int maxPendingQueue = MessengerMediaLimits.MaxAttachmentsPerMessage * 3;
        if (PendingAttachments.Count >= maxPendingQueue)
        {
            AttachStatusMessage = MessengerMediaLimits.CapRefuseEn;
            return false;
        }

        if (PendingAttachments.Any(p => p.IsCloudPick && p.CloudFileEntityId == selection.EntityId))
            return false;

        var mime = selection.MimeType ?? MimeTypes.GetMimeType(selection.Name);
        var draft = new PendingAttachmentDraft
        {
            LocalPath = string.Empty,
            FileName = selection.Name,
            MimeType = mime,
            ByteLength = 0,
            CloudFileEntityId = selection.EntityId,
            IsCloudPick = true,
            UploadCompleted = true
        };
        if (MessengerAttachmentRender.LooksLikeImageMime(mime))
            draft.SendAsFile = false;
        PendingAttachments.Add(draft);
        CommandManager.InvalidateRequerySuggested();
        return true;
    }

    /// <summary>Composer entry used by attach dialog / paste / drop. Returns false when refused (size/cap).</summary>
    public bool TryAddPendingAttachment(string localPath, string? generatedName = null)
    {
        AttachStatusMessage = null;
        if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            return false;

        // Soft queue: send path splits into batches of MaxAttachmentsPerMessage (AT-M10).
        // Hard refuse only at an absurd pending size so the UI cannot runaway.
        const int maxPendingQueue = 50;
        if (PendingAttachments.Count >= maxPendingQueue)
        {
            AttachStatusMessage = MessengerMediaLimits.CapRefuseEn;
            return false;
        }

        var info = new FileInfo(localPath);
        if (MessengerMediaLimits.IsOversize(info.Length))
        {
            AttachStatusMessage = MessengerMediaLimits.OversizeMessageEn;
            return false;
        }

        var name = string.IsNullOrWhiteSpace(generatedName) ? info.Name : generatedName;
        var draft = new PendingAttachmentDraft
        {
            LocalPath = info.FullName,
            FileName = name,
            MimeType = MimeTypes.GetMimeType(name),
            ByteLength = info.Length
        };
        if (MessengerAttachmentRender.LooksLikeImageMime(draft.MimeType))
            draft.LocalThumbnail = TryLoadComposerThumb(info.FullName);
        PendingAttachments.Add(draft);
        CommandManager.InvalidateRequerySuggested();
        return true;
    }

    private static System.Windows.Media.Imaging.BitmapImage? TryLoadComposerThumb(string path)
    {
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.DecodePixelWidth = 80;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private void RemovePendingAttachment(PendingAttachmentDraft? draft)
    {
        if (draft == null) return;
        PendingAttachments.Remove(draft);
        CommandManager.InvalidateRequerySuggested();
    }

    private SemaphoreSlim GateFor(Guid partnerUserId)
    {
        lock (_uploadGates)
        {
            if (!_uploadGates.TryGetValue(partnerUserId, out var gate))
            {
                gate = new SemaphoreSlim(1, 1);
                _uploadGates[partnerUserId] = gate;
            }

            return gate;
        }
    }

    private static bool IsNetworkAvailable() =>
        NetworkInterface.GetIsNetworkAvailable();

    private async Task SendMessageAsync()
    {
        if (SelectedContact == null || _authService.CurrentToken == null) return;
        if (string.IsNullOrWhiteSpace(MessageText) && PendingAttachments.Count == 0) return;

        var myUserId = _authService.CurrentToken.UserId;
        var partnerUserId = SelectedContact.Id;
        var partnerName = SelectedContact.Name;
        var text = MessageText?.Trim() ?? string.Empty;
        var drafts = PendingAttachments.ToList();
        PendingAttachments.Clear();
        MessageText = string.Empty;
        AttachStatusMessage = null;
        CommandManager.InvalidateRequerySuggested();

        // AT-M10: never more than 10 attachment: values on one message — split batches.
        var batches = drafts.Count == 0
            ? new List<IReadOnlyList<PendingAttachmentDraft>> { Array.Empty<PendingAttachmentDraft>() }
            : MessengerAttachmentRender.SplitBatches(drafts, MessengerMediaLimits.MaxAttachmentsPerMessage)
                .ToList();

        for (var i = 0; i < batches.Count; i++)
        {
            var batch = batches[i].ToList();
            // Caption only on the first batch (one logical send from the user).
            var batchText = i == 0 ? text : string.Empty;
            await SendOneOptimisticAsync(myUserId, partnerUserId, partnerName, batchText, batch)
                .ConfigureAwait(true);
        }
    }

    private async Task SendOneOptimisticAsync(
        Guid myUserId,
        Guid partnerUserId,
        string partnerName,
        string text,
        List<PendingAttachmentDraft> drafts)
    {
        var now = DateTime.UtcNow;
        var hasAttachments = drafts.Count > 0;
        var optimistic = new MessageViewModel
        {
            Text = text,
            Timestamp = now,
            IsFromMe = true,
            SenderName = await GetUserLoginAsync(myUserId),
            PartnerUserId = partnerUserId,
            PendingLocalAttachments = drafts,
            AttachmentIds = Array.Empty<Guid>(),
            DeliveryStatus = MessengerSendStateMachine.OnBubbleCreated(hasAttachments)
        };

        if (!_cachedMessages.ContainsKey(partnerUserId))
            _cachedMessages[partnerUserId] = new List<MessageViewModel>();
        _cachedMessages[partnerUserId].Add(optimistic);
        AppendMessageToChat(optimistic);

        if (!IsNetworkAvailable())
        {
            MarkFailed(optimistic, MessengerMediaLimits.OfflineSendFailedEn);
            // AT-M28: draft preserved — restore composer text + chips (first failed batch only).
            if (string.IsNullOrEmpty(MessageText))
                MessageText = text;
            foreach (var d in drafts)
                PendingAttachments.Add(CloneDraftForComposer(d));
            CommandManager.InvalidateRequerySuggested();
            return;
        }

        var gate = GateFor(partnerUserId);
        await gate.WaitAsync().ConfigureAwait(true);
        try
        {
            await ExecuteSendPipelineAsync(optimistic, myUserId, partnerUserId, partnerName, text, drafts, allowFolderReresolve: true)
                .ConfigureAwait(true);
        }
        finally
        {
            gate.Release();
        }
    }

    private static PendingAttachmentDraft CloneDraftForComposer(PendingAttachmentDraft d) =>
        new()
        {
            LocalPath = d.LocalPath,
            FileName = d.FileName,
            MimeType = d.MimeType,
            ByteLength = d.ByteLength,
            SendAsFile = d.SendAsFile,
            LocalThumbnail = d.LocalThumbnail,
            IsCloudPick = d.IsCloudPick,
            CloudFileEntityId = d.CloudFileEntityId,
            UploadCompleted = d.IsCloudPick || d.UploadCompleted
        };

    private async Task ExecuteSendPipelineAsync(
        MessageViewModel optimistic,
        Guid myUserId,
        Guid partnerUserId,
        string partnerName,
        string text,
        List<PendingAttachmentDraft> drafts,
        bool allowFolderReresolve)
    {
        try
        {
            var attachmentIds = new List<Guid>();
            if (drafts.Count > 0)
            {
                var needsUpload = drafts.Any(d => !d.IsCloudPick);
                optimistic.DeliveryStatus = needsUpload
                    ? MessageDeliveryStatus.Uploading
                    : MessageDeliveryStatus.Sending;

                Guid chatFolderId = Guid.Empty;
                if (needsUpload)
                {
                    if (_chatFolders == null)
                        throw new InvalidOperationException("Chat folder resolver is not configured.");

                    try
                    {
                        chatFolderId = await _chatFolders.ResolveOrCreateAsync(myUserId, partnerUserId, partnerName)
                            .ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        MarkFailed(optimistic, MessengerMediaLimits.UploadFailedEn);
                        System.Diagnostics.Debug.WriteLine(ex);
                        return;
                    }
                }

                foreach (var draft in drafts)
                {
                    if (draft.IsCloudPick)
                    {
                        if (draft.CloudFileEntityId == Guid.Empty)
                        {
                            MarkFailed(optimistic, MessengerMediaLimits.UploadFailedEn);
                            return;
                        }

                        try
                        {
                            // M3/M4 — grant on existing entity only (no copy, no AddEntity).
                            if (partnerUserId != myUserId)
                            {
                                await _cloudApi.AddPermissionAsync(
                                        draft.CloudFileEntityId,
                                        partnerUserId,
                                        canWrite: false)
                                    .ConfigureAwait(true);
                            }

                            draft.UploadCompleted = true;
                            attachmentIds.Add(draft.CloudFileEntityId);
                        }
                        catch (Exception ex)
                        {
                            MarkFailed(optimistic, MessengerMediaLimits.UploadFailedEn);
                            System.Diagnostics.Debug.WriteLine(ex);
                            return;
                        }

                        continue;
                    }

                    if (MessengerMediaLimits.IsOversize(draft.ByteLength))
                    {
                        AttachStatusMessage = MessengerMediaLimits.OversizeMessageEn;
                        MarkFailed(optimistic, MessengerMediaLimits.OversizeMessageEn);
                        return;
                    }

                    try
                    {
                        await UploadOneAttachmentAsync(
                                optimistic,
                                draft,
                                chatFolderId,
                                myUserId,
                                partnerUserId)
                            .ConfigureAwait(true);
                    }
                    catch (Exception ex) when (allowFolderReresolve && MessengerChatFolderResolver.IsStaleFolderFailure(ex))
                    {
                        chatFolderId = await _chatFolders!.ReResolveAndCreateAsync(myUserId, partnerUserId, partnerName)
                            .ConfigureAwait(true);
                        // One silent retry of this file create/upload after folder re-resolve.
                        await UploadOneAttachmentAsync(
                                optimistic,
                                draft,
                                chatFolderId,
                                myUserId,
                                partnerUserId)
                            .ConfigureAwait(true);
                    }

                    if (draft.CloudFileEntityId != Guid.Empty)
                        attachmentIds.Add(draft.CloudFileEntityId);
                }

                optimistic.AttachmentIds = attachmentIds;
                optimistic.PendingLocalAttachments = drafts;
                SyncOptimisticAttachmentEntityIds(optimistic, drafts);
                optimistic.DeliveryStatus = MessengerSendStateMachine.OnUploadFinished();
            }
            else
            {
                optimistic.DeliveryStatus = MessageDeliveryStatus.Sending;
            }

            // Existing Sent/Received container bootstrap (text and attachment messages).
            var sentTask = GetOrCreateUserContainer(SystemEntities.Sent, myUserId, "Sent");
            var receivedTask = GetOrCreateUserContainer(SystemEntities.Received, partnerUserId, "Received", grantWrite: true);
            await Task.WhenAll(sentTask, receivedTask).ConfigureAwait(true);
            var sentId = await sentTask.ConfigureAwait(true);
            var receivedId = await receivedTask.ConfigureAwait(true);
            if (sentId == Guid.Empty || receivedId == Guid.Empty)
            {
                MarkFailed(optimistic, MessengerMediaLimits.UploadFailedEn);
                return;
            }

            var values = new List<AddValueContract>
            {
                new() { Type = TypeOfValue.DateTime, Value = optimistic.Timestamp, ParentIds = new[] { SystemEntities.Message } },
                new() { Type = TypeOfValue.String, Value = $"sender:{myUserId}", ParentIds = new[] { SystemEntities.Sent } },
                new() { Type = TypeOfValue.String, Value = $"receiver:{partnerUserId}", ParentIds = new[] { SystemEntities.Received } }
            };
            if (!string.IsNullOrWhiteSpace(text))
            {
                values.Insert(0, new AddValueContract
                {
                    Type = TypeOfValue.String,
                    Value = text,
                    ParentIds = new[] { SystemEntities.Message }
                });
            }

            foreach (var id in attachmentIds)
            {
                values.Add(new AddValueContract
                {
                    Type = TypeOfValue.String,
                    Value = MessengerMediaLimits.FormatAttachmentValue(id),
                    ParentIds = new[] { SystemEntities.Message }
                });
            }

            var addMsg = await _httpClient.PostAsJsonAsync("api/Entities/AddEntity", new AddEntityContract
            {
                Code = "Message",
                ParentIds = new[] { SystemEntities.Message },
                Values = values,
                Permissions = partnerUserId != myUserId
                    ? new List<AddPermissionContract> { new() { PermissionForId = partnerUserId, CanWrite = false } }
                    : null
            }).ConfigureAwait(true);

            if (!addMsg.IsSuccessStatusCode)
            {
                MarkFailed(optimistic, MessengerMediaLimits.UploadFailedEn);
                return;
            }

            try
            {
                var created = await addMsg.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>().ConfigureAwait(true);
                if (created.TryGetProperty("id", out var idEl) && idEl.TryGetGuid(out var newId))
                    optimistic.EntityId = newId;
            }
            catch
            {
                /* keep local id empty */
            }

            optimistic.StatusLine = null;
            optimistic.DeliveryStatus = MessengerSendStateMachine.OnMessageCommitted();

            var payload = new
            {
                senderId = myUserId.ToString(),
                messageText = text,
                timestamp = optimistic.Timestamp
            };
            await NotifyRealtimeAsync("message_sent", partnerUserId.ToString(), payload).ConfigureAwait(true);
            if (partnerUserId != myUserId)
                await NotifyRealtimeAsync("message_sent", myUserId.ToString(), payload).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MarkFailed(optimistic, MessengerMediaLimits.UploadFailedEn);
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private async Task UploadOneAttachmentAsync(
        MessageViewModel optimistic,
        PendingAttachmentDraft draft,
        Guid chatFolderId,
        Guid myUserId,
        Guid partnerUserId)
    {
        if (draft.UploadCompleted)
            return;

        if (draft.CloudFileEntityId == Guid.Empty)
        {
            IReadOnlyList<AddPermissionContract>? perms = null;
            if (partnerUserId != myUserId)
            {
                perms = new[]
                {
                    new AddPermissionContract { PermissionForId = partnerUserId, CanWrite = false }
                };
            }

            draft.CloudFileEntityId = await _cloudApi.AddEntityAsync(
                    CloudEntityCodes.CloudFile,
                    new[] { chatFolderId },
                    draft.FileName,
                    permissions: perms,
                    mimeType: draft.MimeType)
                .ConfigureAwait(true);
        }

        var progress = new Progress<CloudUploadProgress>(p =>
        {
            if (p.Fraction is double f)
                optimistic.UploadProgress = f;
        });

        await using var stream = File.OpenRead(draft.LocalPath);
        await _cloudApi.AddFileAsync(
                draft.CloudFileEntityId,
                draft.FileName,
                stream,
                draft.MimeType,
                progress)
            .ConfigureAwait(true);
        draft.UploadCompleted = true;
        optimistic.UploadProgress = 1;
    }

    private static void SyncOptimisticAttachmentEntityIds(
        MessageViewModel optimistic,
        List<PendingAttachmentDraft> drafts)
    {
        for (var i = 0; i < drafts.Count && i < optimistic.Attachments.Count; i++)
        {
            if (drafts[i].CloudFileEntityId != Guid.Empty)
                optimistic.Attachments[i].EntityId = drafts[i].CloudFileEntityId;
        }
    }

    private void MarkFailed(MessageViewModel optimistic, string statusLine)
    {
        optimistic.DeliveryStatus = MessengerSendStateMachine.OnFailure();
        optimistic.StatusLine = statusLine;
    }

    private async Task RetryFailedSendAsync(MessageViewModel? optimistic)
    {
        if (optimistic == null || !optimistic.ShowFailedActions) return;
        if (_authService.CurrentToken == null) return;

        var myUserId = _authService.CurrentToken.UserId;
        var partnerUserId = optimistic.PartnerUserId != Guid.Empty
            ? optimistic.PartnerUserId
            : SelectedContact?.Id ?? Guid.Empty;
        if (partnerUserId == Guid.Empty) return;

        if (!IsNetworkAvailable())
        {
            MarkFailed(optimistic, MessengerMediaLimits.OfflineSendFailedEn);
            return;
        }

        var partnerName = SelectedContact?.Id == partnerUserId
            ? SelectedContact.Name
            : (Contacts.FirstOrDefault(c => c.Id == partnerUserId)?.Name ?? "Chat");

        optimistic.StatusLine = null;
        optimistic.DeliveryStatus = MessengerSendStateMachine.OnRetry();

        var drafts = optimistic.PendingLocalAttachments ?? new List<PendingAttachmentDraft>();
        // Ensure entity ids are reused (AT-M26 / AT-M27).
        foreach (var d in drafts)
        {
            d.CloudFileEntityId = MessengerSendStateMachine.ResolveEntityIdForRetry(d.CloudFileEntityId, null);
            // Re-upload if previous attempt failed before/during addFile; keep entity id.
            if (!d.UploadCompleted && d.CloudFileEntityId != Guid.Empty)
            {
                // stay Uploading and call addFile again on same id
            }
        }

        var gate = GateFor(partnerUserId);
        await gate.WaitAsync().ConfigureAwait(true);
        try
        {
            await ExecuteSendPipelineAsync(
                    optimistic,
                    myUserId,
                    partnerUserId,
                    partnerName,
                    optimistic.Text ?? string.Empty,
                    drafts,
                    allowFolderReresolve: true)
                .ConfigureAwait(true);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task CancelFailedSendAsync(MessageViewModel? optimistic)
    {
        if (optimistic == null || !optimistic.ShowFailedActions) return;

        foreach (var draft in optimistic.PendingLocalAttachments ?? new List<PendingAttachmentDraft>())
        {
            if (draft.CloudFileEntityId == Guid.Empty) continue;
            try
            {
                await _cloudApi.DeleteEntityAsync(draft.CloudFileEntityId).ConfigureAwait(true);
            }
            catch
            {
                /* best-effort orphan cleanup */
            }
        }

        RemoveOptimisticMessage(optimistic.PartnerUserId != Guid.Empty
            ? optimistic.PartnerUserId
            : SelectedContact?.Id ?? Guid.Empty, optimistic);
    }

    /// <summary>Keep in-flight / Failed outgoing bubbles across poll refresh (AT-M29: Failed does not survive process restart).</summary>
    private List<MessageViewModel> MergeInFlightLocalMessages(
        Guid partnerUserId,
        List<MessageViewModel> fromServer)
    {
        if (!_cachedMessages.TryGetValue(partnerUserId, out var previous) || previous.Count == 0)
            return fromServer;

        var locals = previous
            .Where(m =>
                m.IsFromMe
                && m.DeliveryStatus is MessageDeliveryStatus.Uploading
                    or MessageDeliveryStatus.Sending
                    or MessageDeliveryStatus.Failed)
            .ToList();
        if (locals.Count == 0)
            return fromServer;

        var serverIds = new HashSet<Guid>(fromServer.Where(m => m.EntityId != Guid.Empty).Select(m => m.EntityId));
        foreach (var local in locals)
        {
            if (local.EntityId != Guid.Empty && serverIds.Contains(local.EntityId))
                continue;
            fromServer.Add(local);
        }

        return fromServer
            .OrderBy(x => x.Timestamp)
            .ThenBy(x => x.EntityId)
            .ToList();
    }
}
