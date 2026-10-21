using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Live verify of Microsoft.AspNetCore.SignalR.Client 10.0.13 against LocalStand /hubs/commands
/// (same client package + <see cref="SignalRService"/> path as Communicator.Windows).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Live")]
public sealed class SignalRCommandsHubLiveTests
{
    [LiveFact]
    public async Task Connect_Receives_MessageSent_DataChanged_And_MessageRead()
    {
        var baseUrl = LiveApiTarget.RequireBaseUrl();
        using var http = new HttpClient();
        IntegrationTestHttp.ConfigureClient(http);

        var primary = await LoginAsync(http, MessengerTestCredentials.PrimaryLogin, MessengerTestCredentials.PrimaryPassword);
        Assert.True(string.IsNullOrEmpty(primary.Error), primary.Error ?? "primary login failed");
        Assert.False(string.IsNullOrEmpty(primary.AccessToken));

        var partnerPassword = Environment.GetEnvironmentVariable("PROTOLINK_TEST_PARTNER_PASSWORD")
            ?? MessengerTestCredentials.PrimaryPassword;
        var partner = await LoginAsync(http, MessengerTestCredentials.PartnerLogin, partnerPassword);
        Assert.True(string.IsNullOrEmpty(partner.Error), partner.Error ?? "partner login failed");
        Assert.False(string.IsNullOrEmpty(partner.AccessToken));

        await using var partnerHub = new SignalRService();
        var received = new ConcurrentQueue<string?>();
        var messageSent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dataChanged = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var messageRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        partnerHub.RefreshRequested += (commandType, _) =>
        {
            received.Enqueue(commandType);
            if (string.Equals(commandType, "message_sent", StringComparison.OrdinalIgnoreCase))
                messageSent.TrySetResult(true);
            if (string.Equals(commandType, "data_changed", StringComparison.OrdinalIgnoreCase))
                dataChanged.TrySetResult(true);
            if (string.Equals(commandType, "message_read", StringComparison.OrdinalIgnoreCase))
                messageRead.TrySetResult(true);
        };

        await partnerHub.ConnectAsync(baseUrl, () => Task.FromResult<string?>(partner.AccessToken));
        Assert.True(partnerHub.IsConnected, "partner SignalR hub did not connect to /hubs/commands");

        // Give SubscribeToCommands a beat before pushes (group membership).
        await Task.Delay(500);

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", primary.AccessToken);

        var marker = Guid.NewGuid().ToString("N");
        var sentNotify = await http.PostAsJsonAsync("api/commands/send", new
        {
            commandType = "message_sent",
            targetUserId = partner.UserId.ToString(),
            parameters = new { senderId = primary.UserId.ToString(), messageText = marker, source = "signalr-live-test" }
        }, IntegrationTestHttp.JsonOptions);
        await IntegrationTestHttp.AssertSuccessAsync(sentNotify, "POST api/commands/send message_sent");

        var changedNotify = await http.PostAsJsonAsync("api/commands/send", new
        {
            commandType = "data_changed",
            targetUserId = partner.UserId.ToString(),
            parameters = new { source = "signalr-live-test", marker }
        }, IntegrationTestHttp.JsonOptions);
        await IntegrationTestHttp.AssertSuccessAsync(changedNotify, "POST api/commands/send data_changed");

        // Partner acknowledges read → primary listens for message_read (receipts path).
        await using var primaryHub = new SignalRService();
        var primaryRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        primaryHub.RefreshRequested += (commandType, _) =>
        {
            if (string.Equals(commandType, "message_read", StringComparison.OrdinalIgnoreCase))
                primaryRead.TrySetResult(true);
        };
        await primaryHub.ConnectAsync(baseUrl, () => Task.FromResult<string?>(primary.AccessToken));
        Assert.True(primaryHub.IsConnected, "primary SignalR hub did not connect");
        await Task.Delay(500);

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", partner.AccessToken);
        var readNotify = await http.PostAsJsonAsync("api/commands/send", new
        {
            commandType = "message_read",
            targetUserId = primary.UserId.ToString(),
            parameters = new { readerId = partner.UserId.ToString(), marker }
        }, IntegrationTestHttp.JsonOptions);
        await IntegrationTestHttp.AssertSuccessAsync(readNotify, "POST api/commands/send message_read");

        // Also push message_read to partner so the same hub under test sees receipts traffic.
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", primary.AccessToken);
        var readToPartner = await http.PostAsJsonAsync("api/commands/send", new
        {
            commandType = "message_read",
            targetUserId = partner.UserId.ToString(),
            parameters = new { readerId = primary.UserId.ToString(), marker }
        }, IntegrationTestHttp.JsonOptions);
        await IntegrationTestHttp.AssertSuccessAsync(readToPartner, "POST api/commands/send message_read→partner");

        var timeout = TimeSpan.FromSeconds(20);
        Assert.True(await WaitAsync(messageSent.Task, timeout), $"message_sent not received. Got: [{string.Join(", ", received)}]");
        Assert.True(await WaitAsync(dataChanged.Task, timeout), $"data_changed not received. Got: [{string.Join(", ", received)}]");
        Assert.True(await WaitAsync(messageRead.Task, timeout), $"message_read not received on partner hub. Got: [{string.Join(", ", received)}]");
        Assert.True(await WaitAsync(primaryRead.Task, timeout), "message_read not received on primary hub (receipts)");
    }

    [LiveFact]
    public async Task SignalRService_Reconnects_After_StopAndStart()
    {
        // Exercises the same ConnectAsync path used after an intentional disconnect.
        var baseUrl = LiveApiTarget.RequireBaseUrl();
        using var http = new HttpClient();
        IntegrationTestHttp.ConfigureClient(http);

        var login = await LoginAsync(http, MessengerTestCredentials.PrimaryLogin, MessengerTestCredentials.PrimaryPassword);
        Assert.True(string.IsNullOrEmpty(login.Error), login.Error ?? "login failed");
        Assert.False(string.IsNullOrEmpty(login.AccessToken));

        await using var hub = new SignalRService();
        await hub.ConnectAsync(baseUrl, () => Task.FromResult<string?>(login.AccessToken));
        Assert.True(hub.IsConnected);

        await hub.DisconnectAsync();
        Assert.False(hub.IsConnected);

        await hub.ConnectAsync(baseUrl, () => Task.FromResult<string?>(login.AccessToken));
        Assert.True(hub.IsConnected, "hub did not reconnect after Disconnect+Connect");

        // Push a command after re-connect to prove the new socket receives traffic.
        var got = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.RefreshRequested += (commandType, _) =>
        {
            if (string.Equals(commandType, "data_changed", StringComparison.OrdinalIgnoreCase))
                got.TrySetResult(true);
        };

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var notify = await http.PostAsJsonAsync("api/commands/send", new
        {
            commandType = "data_changed",
            targetUserId = login.UserId.ToString(),
            parameters = new { source = "signalr-reconnect-test" }
        }, IntegrationTestHttp.JsonOptions);
        await IntegrationTestHttp.AssertSuccessAsync(notify, "POST data_changed after reconnect");
        Assert.True(await WaitAsync(got.Task, TimeSpan.FromSeconds(20)), "no data_changed after reconnect");
    }

    /// <summary>
    /// Restarts LocalStand <c>ProtoLink.Api</c> and asserts <see cref="SignalRService"/> fires
    /// <c>reconnected</c> via <c>WithAutomaticReconnect</c>. Opt-in:
    /// <c>PROTOLINK_SIGNALR_API_RESTART=1</c> (disrupts shared LocalStand briefly).
    /// </summary>
    [SignalRApiRestartLiveFact]
    public async Task SignalRService_AutomaticReconnect_After_LocalStandApiRestart()
    {
        var baseUrl = LiveApiTarget.RequireBaseUrl();
        using var http = new HttpClient();
        IntegrationTestHttp.ConfigureClient(http);

        var login = await LoginAsync(http, MessengerTestCredentials.PrimaryLogin, MessengerTestCredentials.PrimaryPassword);
        Assert.True(string.IsNullOrEmpty(login.Error), login.Error ?? "login failed");
        Assert.False(string.IsNullOrEmpty(login.AccessToken));

        await using var hub = new SignalRService();
        var reconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.RefreshRequested += (commandType, _) =>
        {
            if (string.Equals(commandType, "reconnected", StringComparison.OrdinalIgnoreCase))
                reconnected.TrySetResult(true);
        };

        await hub.ConnectAsync(baseUrl, () => Task.FromResult<string?>(login.AccessToken));
        Assert.True(hub.IsConnected, "initial hub connect failed");

        RestartLocalStandApi();

        Assert.True(
            await WaitAsync(reconnected.Task, TimeSpan.FromSeconds(90)),
            "SignalRService did not raise reconnected within 90s after LocalStand API restart");
        Assert.True(hub.IsConnected, "hub not Connected after automatic reconnect");

        // Traffic after reconnect.
        var got = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.RefreshRequested += (commandType, _) =>
        {
            if (string.Equals(commandType, "data_changed", StringComparison.OrdinalIgnoreCase))
                got.TrySetResult(true);
        };
        // Token may still be valid; re-login if needed.
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var notify = await http.PostAsJsonAsync("api/commands/send", new
        {
            commandType = "data_changed",
            targetUserId = login.UserId.ToString(),
            parameters = new { source = "signalr-api-restart-test" }
        }, IntegrationTestHttp.JsonOptions);
        if (!notify.IsSuccessStatusCode)
        {
            login = await LoginAsync(http, MessengerTestCredentials.PrimaryLogin, MessengerTestCredentials.PrimaryPassword);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
            notify = await http.PostAsJsonAsync("api/commands/send", new
            {
                commandType = "data_changed",
                targetUserId = login.UserId.ToString(),
                parameters = new { source = "signalr-api-restart-test" }
            }, IntegrationTestHttp.JsonOptions);
        }
        await IntegrationTestHttp.AssertSuccessAsync(notify, "POST data_changed after API restart reconnect");
        Assert.True(await WaitAsync(got.Task, TimeSpan.FromSeconds(20)), "no data_changed after API restart reconnect");
    }

    private static void RestartLocalStandApi()
    {
        var apiDir = @"D:\- Projects\ProtoLink.Api\ProtoLink.Api";
        var exe = Path.Combine(apiDir, @"bin\Debug\net9.0\ProtoLink.Api.exe");
        Assert.True(File.Exists(exe), "LocalStand ProtoLink.Api.exe missing at " + exe);

        foreach (var p in System.Diagnostics.Process.GetProcessesByName("ProtoLink.Api"))
        {
            try { p.Kill(entireProcessTree: true); p.WaitForExit(15000); }
            catch { /* already exiting */ }
        }

        // Parent `dotnet run` may linger; leave it — we start the built exe directly.
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exe,
            Arguments = "--urls http://localhost:5000",
            WorkingDirectory = apiDir,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "LocalStand";
        var started = System.Diagnostics.Process.Start(psi);
        Assert.NotNull(started);

        // Wait until HTTP answers again.
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var resp = probe.GetAsync("http://localhost:5000/openapi/v1.json").GetAwaiter().GetResult();
                if (resp.IsSuccessStatusCode) return;
            }
            catch { /* starting */ }
            Thread.Sleep(1000);
        }
        Assert.Fail("LocalStand API did not come back on :5000 within 60s");
    }

    private static async Task<bool> WaitAsync(Task task, TimeSpan timeout) =>
        await Task.WhenAny(task, Task.Delay(timeout)) == task && task.IsCompletedSuccessfully;

    private static async Task<LoginResult> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync(
            "api/Authentication/login",
            new LoginContract { Login = email, Password = password },
            IntegrationTestHttp.JsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<LoginResult>(body, IntegrationTestHttp.JsonOptions)
            ?? new LoginResult { Error = "empty response" };
        if (!response.IsSuccessStatusCode && string.IsNullOrEmpty(result.Error))
            result.Error = body;
        return result;
    }
}

/// <summary>Live + opt-in API process recycle (shared LocalStand disruption).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class SignalRApiRestartLiveFactAttribute : FactAttribute
{
    public SignalRApiRestartLiveFactAttribute()
    {
        var reason = LiveApiTarget.SkipReasonFromEnvironment();
        if (reason != null)
        {
            Skip = reason;
            return;
        }

        if (!string.Equals(
                Environment.GetEnvironmentVariable("PROTOLINK_SIGNALR_API_RESTART")?.Trim(),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Skipped: set PROTOLINK_SIGNALR_API_RESTART=1 to recycle LocalStand ProtoLink.Api for WithAutomaticReconnect verify.";
        }
    }
}
