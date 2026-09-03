using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// End-to-end messenger API checks (login, contacts, send, load messages) matching MessengerViewModel behavior.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessengerIntegrationTests : IAsyncLifetime
{
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _client = new HttpClient();
        IntegrationTestHttp.ConfigureClient(_client);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Login_GetContacts_SendToPartner_GetMessages_ContainsSentText()
    {
        var loginTask = LoginAsync(_client, MessengerTestCredentials.PrimaryLogin, MessengerTestCredentials.PrimaryPassword);
        var partnerTask = GetUserByLoginAsync(_client, MessengerTestCredentials.PartnerLogin);
        await Task.WhenAll(loginTask, partnerTask);

        var login = await loginTask;
        Assert.True(string.IsNullOrEmpty(login.Error), login.Error ?? "Login failed");
        Assert.False(string.IsNullOrEmpty(login.AccessToken));

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var partnerUser = await partnerTask;
        Assert.NotNull(partnerUser);
        var partnerId = partnerUser!.Value;

        _ = await LoadContactsAsync(_client, login.UserId);

        var marker = $"[integration-test] {Guid.NewGuid():N}";
        var sentAt = DateTime.UtcNow;

        var messageEntityId = await SendMessageAsync(_client, login.UserId, partnerId, marker, sentAt);

        var messages = await LoadConversationMessagesAsync(_client, login.UserId, partnerId, messageEntityId, marker);
        Assert.True(
            messages.Exists(m => m.Text == marker && m.IsFromMe),
            $"Sent marker not in filtered conversation. myUserId={login.UserId} partnerId={partnerId}. " +
            $"LoadedCount={messages.Count}. Loaded: [{string.Join(" | ", messages.Select(m => $"{m.Text} (me={m.IsFromMe})"))}]");
    }

    private static async Task<LoginResult> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("api/Authentication/login", new LoginContract { Login = email, Password = password }, IntegrationTestHttp.JsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<LoginResult>(body, IntegrationTestHttp.JsonOptions) ?? new LoginResult { Error = "empty response" };
        if (!response.IsSuccessStatusCode && string.IsNullOrEmpty(result.Error))
            result.Error = body;
        return result;
    }

    private static async Task<Guid?> GetUserByLoginAsync(HttpClient client, string login)
    {
        var encoded = Uri.EscapeDataString(login);
        var response = await client.GetAsync($"api/Authentication/GetUser?login={encoded}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, IntegrationTestHttp.BuildFailureMessage($"GET api/Authentication/GetUser?login={encoded}", response, body));
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("id", out var idEl)) return null;
        return idEl.GetGuid();
    }

    private static async Task<List<ContactRow>> LoadContactsAsync(HttpClient client, Guid userId)
    {
        var list = new List<ContactRow>();

        var userContactsResponse = await client.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
        {
            ParentIds = new[] { SystemEntities.Contacts, userId }
        }, IntegrationTestHttp.JsonOptions);
        var ucBody = await IntegrationTestHttp.AssertSuccessAsync(userContactsResponse, "GetEntities UserContacts row (Contacts+userId)");
        var userContactsResults = JsonSerializer.Deserialize<List<GetEntitiesResult>>(ucBody, IntegrationTestHttp.JsonOptions);
        var userContacts = userContactsResults?.FirstOrDefault();

        Guid userContactsId;
        if (userContacts == null)
        {
            var addResponse = await client.PostAsJsonAsync("api/Entities/AddEntity", new AddEntityContract
            {
                Code = "UserContacts",
                ParentIds = new[] { SystemEntities.Contacts, userId }
            }, IntegrationTestHttp.JsonOptions);
            var addBody = await IntegrationTestHttp.AssertSuccessAsync(addResponse, "AddEntity UserContacts");
            var addResult = JsonSerializer.Deserialize<JsonElement>(addBody, IntegrationTestHttp.JsonOptions);
            userContactsId = addResult.GetProperty("id").GetGuid();
        }
        else
            userContactsId = userContacts.Id;

        var contactsResponse = await client.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
        {
            ParentIds = new[] { userContactsId },
            IncludeValues = true
        }, IntegrationTestHttp.JsonOptions);
        var contactsBody = await IntegrationTestHttp.AssertSuccessAsync(contactsResponse, "GetEntities contact list (IncludeValues)");
        var contactsResults = JsonSerializer.Deserialize<List<GetEntitiesResult>>(contactsBody, IntegrationTestHttp.JsonOptions);
        if (contactsResults == null) return list;

        foreach (var c in contactsResults)
        {
            Guid contactUserId = Guid.Empty;
            if (c.Values != null)
            {
                foreach (var val in c.Values)
                {
                    if (val is EntityValue ev && ev.Type == "StringValue")
                    {
                        var s = ev.Value?.ToString();
                        if (!string.IsNullOrEmpty(s) && s.StartsWith("userid:", StringComparison.Ordinal) && Guid.TryParse(s.AsSpan(7), out var uid))
                        {
                            contactUserId = uid;
                            break;
                        }
                    }
                }
            }
            if (contactUserId == Guid.Empty && !string.IsNullOrEmpty(c.Code) && Guid.TryParse(c.Code, out var codeUid))
                contactUserId = codeUid;
            if (contactUserId != Guid.Empty)
                list.Add(new ContactRow(contactUserId, c.Code ?? ""));
        }

        return list;
    }

    /// <summary>Same parent slots as <c>MessengerViewModel.SendMessageAsync</c>: API dedupes multiple string values with empty parents into one row.</summary>
    private static async Task<Guid> SendMessageAsync(HttpClient client, Guid myUserId, Guid partnerUserId, string text, DateTime utcNow)
    {
        var sentTask = GetOrCreateUserContainerAsync(client, SystemEntities.Sent, myUserId, "Sent", grantWrite: false);
        var receivedTask = GetOrCreateUserContainerAsync(client, SystemEntities.Received, partnerUserId, "Received", grantWrite: true);
        await Task.WhenAll(sentTask, receivedTask);
        var sentId = await sentTask;
        var receivedId = await receivedTask;
        Assert.NotEqual(Guid.Empty, sentId);
        Assert.NotEqual(Guid.Empty, receivedId);

        var addMsg = await client.PostAsJsonAsync("api/Entities/AddEntity", new AddEntityContract
        {
            Code = "Message",
            ParentIds = new[] { SystemEntities.Message },
            Values = new List<AddValueContract>
            {
                new() { Type = TypeOfValue.String, Value = text, ParentIds = new[] { SystemEntities.Message } },
                new() { Type = TypeOfValue.DateTime, Value = utcNow, ParentIds = new[] { SystemEntities.Message } },
                new() { Type = TypeOfValue.String, Value = $"sender:{myUserId}", ParentIds = new[] { SystemEntities.Sent } },
                new() { Type = TypeOfValue.String, Value = $"receiver:{partnerUserId}", ParentIds = new[] { SystemEntities.Received } }
            },
            Permissions = partnerUserId != myUserId ? new List<AddPermissionContract> { new() { PermissionForId = partnerUserId, CanWrite = false } } : null
        }, IntegrationTestHttp.JsonOptions);
        var createdBody = await IntegrationTestHttp.AssertSuccessAsync(addMsg, "AddEntity Message");
        var created = JsonSerializer.Deserialize<JsonElement>(createdBody, IntegrationTestHttp.JsonOptions);
        var messageEntityId = created.GetProperty("id").GetGuid();

        var notify = await client.PostAsJsonAsync("api/commands/send", new
        {
            commandType = "message_sent",
            targetUserId = partnerUserId.ToString(),
            parameters = new { senderId = myUserId.ToString(), messageText = text, timestamp = utcNow }
        }, IntegrationTestHttp.JsonOptions);
        await IntegrationTestHttp.AssertSuccessAsync(notify, "POST api/commands/send message_sent");

        return messageEntityId;
    }

    private static async Task<Guid> GetOrCreateUserContainerAsync(HttpClient client, Guid systemParentId, Guid userId, string code, bool grantWrite)
    {
        var response = await client.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
        {
            ParentIds = new[] { systemParentId, userId }
        }, IntegrationTestHttp.JsonOptions);
        var body = await IntegrationTestHttp.AssertSuccessAsync(response, $"GetEntities {code} container");
        var results = JsonSerializer.Deserialize<List<GetEntitiesResult>>(body, IntegrationTestHttp.JsonOptions);
        if (results?.Any() == true)
            return results.First().Id;

        var addResponse = await client.PostAsJsonAsync("api/Entities/AddEntity", new AddEntityContract
        {
            Code = code,
            ParentIds = new[] { systemParentId, userId }
        }, IntegrationTestHttp.JsonOptions);
        var addBody = await IntegrationTestHttp.AssertSuccessAsync(addResponse, $"AddEntity {code} container");
        var addResult = JsonSerializer.Deserialize<JsonElement>(addBody, IntegrationTestHttp.JsonOptions);
        var createdId = addResult.GetProperty("id").GetGuid();
        if (grantWrite && createdId != Guid.Empty)
        {
            var permResp = await client.PostAsJsonAsync("api/Entities/AddPermission", new AddPermissionContract
            {
                Id = createdId,
                PermissionForId = userId,
                CanWrite = true
            }, IntegrationTestHttp.JsonOptions);
            await IntegrationTestHttp.AssertSuccessAsync(permResp, $"AddPermission write for {code} container");
        }
        return createdId;
    }

    /// <summary>One <c>GetEntities</c> under Message (same as MessengerViewModel). Optionally asserts the newly created row is present with body/sender/receiver.</summary>
    private static async Task<List<MessageRow>> LoadConversationMessagesAsync(
        HttpClient client,
        Guid myUserId,
        Guid partnerUserId,
        Guid? verifyMessageEntityId = null,
        string? verifyMarker = null)
    {
        var myStr = myUserId.ToString();
        var partnerStr = partnerUserId.ToString();

        var response = await client.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
        {
            ParentIds = new[] { SystemEntities.Message },
            IncludeValues = true,
            Skip = 0,
            Take = 5000
        }, IntegrationTestHttp.JsonOptions);
        var feedBody = await IntegrationTestHttp.AssertSuccessAsync(response, "GetEntities Message feed (IncludeValues, Take=5000)");
        var all = JsonSerializer.Deserialize<List<GetEntitiesResult>>(feedBody, IntegrationTestHttp.JsonOptions);
        if (all == null) return new List<MessageRow>();

        if (verifyMessageEntityId.HasValue && verifyMarker != null)
        {
            var probe = all.FirstOrDefault(e => e.Id == verifyMessageEntityId.Value);
            Assert.True(
                probe != null,
                $"New message entity {verifyMessageEntityId} not returned in the Message feed (count={all.Count}).");
            GetBodySenderReceiver(probe!, out var body, out var sender, out var receiver);
            Assert.Equal(myStr, sender);
            Assert.Equal(partnerStr, receiver);
            Assert.Equal(verifyMarker, body);
        }

        var sent = new List<GetEntitiesResult>();
        var received = new List<GetEntitiesResult>();
        foreach (var msg in all)
        {
            GetSenderReceiverStrings(msg, out var sender, out var receiver);
            if (sender == myStr && receiver == partnerStr) sent.Add(msg);
            else if (sender == partnerStr && receiver == myStr) received.Add(msg);
        }

        var list = new List<MessageRow>();
        foreach (var msg in sent)
        {
            var row = ParseMessage(msg, isFromMe: true);
            if (row != null) list.Add(row);
        }
        foreach (var msg in received)
        {
            var row = ParseMessage(msg, isFromMe: false);
            if (row != null) list.Add(row);
        }
        return list.OrderBy(x => x.Timestamp).ToList();
    }

    private static MessageRow? ParseMessage(GetEntitiesResult message, bool isFromMe)
    {
        if (message.Values == null) return null;
        var text = "";
        var date = DateTime.MinValue;
        foreach (var val in message.Values)
        {
            if (val is EntityValue ev)
            {
                if (ev.Type == "StringValue")
                {
                    var s = ev.Value?.ToString() ?? "";
                    if (!s.StartsWith("sender:", StringComparison.Ordinal) && !s.StartsWith("receiver:", StringComparison.Ordinal))
                        text = s;
                }
                else if (ev.Type == "DateTimeValue" && ev.Value != null)
                {
                    var ds = ev.Value.ToString();
                    if (ds != null) DateTime.TryParse(ds, out date);
                }
            }
        }
        return new MessageRow(text, date, isFromMe);
    }

    private static void GetSenderReceiverStrings(GetEntitiesResult msg, out string? sender, out string? receiver)
    {
        sender = null;
        receiver = null;
        if (msg.Values == null) return;
        foreach (var val in msg.Values)
        {
            if (val is not EntityValue ev || ev.Type != "StringValue") continue;
            var s = ev.Value?.ToString();
            if (s == null) continue;
            if (s.StartsWith("sender:", StringComparison.Ordinal)) sender = s.Substring(7);
            else if (s.StartsWith("receiver:", StringComparison.Ordinal)) receiver = s.Substring(9);
        }
    }

    private static void GetBodySenderReceiver(GetEntitiesResult msg, out string? body, out string? sender, out string? receiver)
    {
        body = null;
        sender = null;
        receiver = null;
        if (msg.Values == null) return;
        foreach (var val in msg.Values)
        {
            if (val is not EntityValue ev || ev.Type != "StringValue") continue;
            var s = ev.Value?.ToString();
            if (s == null) continue;
            if (s.StartsWith("sender:", StringComparison.Ordinal)) sender = s.Substring(7);
            else if (s.StartsWith("receiver:", StringComparison.Ordinal)) receiver = s.Substring(9);
            else body = s;
        }
    }

    private sealed record ContactRow(Guid UserId, string CodeOrName);

    private sealed record MessageRow(string Text, DateTime Timestamp, bool IsFromMe);
}
