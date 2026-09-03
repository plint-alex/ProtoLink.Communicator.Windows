using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using Xunit;
using Xunit.Abstractions;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Mirrors "open app → messenger loads → select first contact → messages appear" and records per-request timing.
/// Historical slowness was dominated by API <c>GetEntities</c> with <c>IncludeValues</c>: one DB round-trip per entity (N+1).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Scenario", "MessengerPerformance")]
public sealed class MessengerOpenSelectContactScenarioTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private HttpClient _client = null!;

    public MessengerOpenSelectContactScenarioTests(ITestOutputHelper output) => _output = output;

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
    public async Task OpenMessenger_SelectFirstContact_TimingsLogged()
    {
        var swTotal = Stopwatch.StartNew();

        var loginTask = LoginAsync();
        var partnerTask = GetPartnerIdAsync();
        await Task.WhenAll(loginTask, partnerTask);
        var login = await loginTask;
        Assert.True(string.IsNullOrEmpty(login.Error), login.Error);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var partnerId = await partnerTask;
        _output.WriteLine($"[auth] login + partner resolve: {swTotal.ElapsedMilliseconds} ms");

        var userId = login.UserId;

        var t1 = Stopwatch.StartNew();
        var userContactsResponse = await _client.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
        {
            ParentIds = new[] { SystemEntities.Contacts, userId }
        }, IntegrationTestHttp.JsonOptions);
        var ucBody = await IntegrationTestHttp.AssertSuccessAsync(userContactsResponse, "GetEntities(Contacts+user)");
        var userContactsResults = JsonSerializer.Deserialize<List<GetEntitiesResult>>(ucBody, IntegrationTestHttp.JsonOptions);
        var userContacts = userContactsResults?.FirstOrDefault();
        t1.Stop();
        _output.WriteLine($"[init] GetEntities(Contacts+user) {t1.ElapsedMilliseconds} ms");

        Guid userContactsId;
        if (userContacts == null)
        {
            var addResponse = await _client.PostAsJsonAsync("api/Entities/AddEntity", new AddEntityContract
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

        var t2 = Stopwatch.StartNew();
        var contactsResponse = await _client.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
        {
            ParentIds = new[] { userContactsId },
            IncludeValues = true
        }, IntegrationTestHttp.JsonOptions);
        var contactsBody = await IntegrationTestHttp.AssertSuccessAsync(contactsResponse, "GetEntities(contact list, IncludeValues)");
        var contactsResults = JsonSerializer.Deserialize<List<GetEntitiesResult>>(contactsBody, IntegrationTestHttp.JsonOptions);
        t2.Stop();
        _output.WriteLine($"[init] GetEntities(contact list, IncludeValues) rows={contactsResults?.Count ?? 0} {t2.ElapsedMilliseconds} ms (was N GetEntity calls per row server-side before batch fix)");

        var t3 = Stopwatch.StartNew();
        var selfResponse = await _client.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
        {
            ParentIds = new[] { userContactsId, userId }
        }, IntegrationTestHttp.JsonOptions);
        await IntegrationTestHttp.AssertSuccessAsync(selfResponse, "GetEntities(self row)");
        t3.Stop();
        _output.WriteLine($"[init] GetEntities(self row) {t3.ElapsedMilliseconds} ms");

        Guid? firstContactUserId = null;
        if (contactsResults != null)
        {
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
                if (contactUserId != Guid.Empty && contactUserId != userId)
                {
                    firstContactUserId = contactUserId;
                    break;
                }
            }
        }

        if (firstContactUserId == null)
            firstContactUserId = partnerId;

        var t4 = Stopwatch.StartNew();
        var msgResponse = await _client.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
        {
            ParentIds = new[] { SystemEntities.Message },
            IncludeValues = true,
            Skip = 0,
            Take = 5000
        }, IntegrationTestHttp.JsonOptions);
        var msgBody = await IntegrationTestHttp.AssertSuccessAsync(msgResponse, "GetEntities(Message, IncludeValues, Take=5000)");
        var all = JsonSerializer.Deserialize<List<GetEntitiesResult>>(msgBody, IntegrationTestHttp.JsonOptions);
        t4.Stop();
        _output.WriteLine($"[select contact] GetEntities(Message, IncludeValues, Take=5000) rows={all?.Count ?? 0} {t4.ElapsedMilliseconds} ms (dominant cost when many messages exist)");

        var myStr = userId.ToString();
        var partnerStr = firstContactUserId.Value.ToString();
        var matchCount = 0;
        if (all != null)
        {
            foreach (var msg in all)
            {
                if (msg.Values == null) continue;
                string? sender = null, receiver = null;
                foreach (var val in msg.Values)
                {
                    if (val is EntityValue ev && ev.Type == "StringValue")
                    {
                        var s = ev.Value?.ToString();
                        if (s == null) continue;
                        if (s.StartsWith("sender:", StringComparison.Ordinal)) sender = s.Substring(7);
                        else if (s.StartsWith("receiver:", StringComparison.Ordinal)) receiver = s.Substring(9);
                    }
                }
                if (sender == myStr && receiver == partnerStr) matchCount++;
                else if (sender == partnerStr && receiver == myStr) matchCount++;
            }
        }

        swTotal.Stop();
        _output.WriteLine($"[summary] conversation rows for first contact: {matchCount}; total scenario: {swTotal.ElapsedMilliseconds} ms");
        Assert.NotNull(all);
    }

    private async Task<LoginResult> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync("api/Authentication/login", new LoginContract
        {
            Login = MessengerTestCredentials.PrimaryLogin,
            Password = MessengerTestCredentials.PrimaryPassword
        }, IntegrationTestHttp.JsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<LoginResult>(body, IntegrationTestHttp.JsonOptions) ?? new LoginResult { Error = "empty" };
        if (!response.IsSuccessStatusCode && string.IsNullOrEmpty(result.Error))
            result.Error = body;
        return result;
    }

    private async Task<Guid> GetPartnerIdAsync()
    {
        var encoded = Uri.EscapeDataString(MessengerTestCredentials.PartnerLogin);
        var response = await _client.GetAsync($"api/Authentication/GetUser?login={encoded}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, IntegrationTestHttp.BuildFailureMessage($"GET api/Authentication/GetUser?login={encoded}", response, body));
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("id", out var idEl), "Partner GetUser JSON missing id");
        return idEl.GetGuid();
    }
}
