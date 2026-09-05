using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// Shared app-wide SignalR connection to /hubs/commands.
/// Any ReceiveCommand triggers <see cref="RefreshRequested"/> so Messenger, Cloud, and Notes can refresh.
/// </summary>
public sealed class SignalRService : IAsyncDisposable
{
    private HubConnection? _connection;
    private string _hubUrl = "";
    private Func<Task<string?>>? _accessTokenProvider;

    public event Action<string?>? RefreshRequested;

    public bool IsConnected =>
        _connection?.State == HubConnectionState.Connected;

    public async Task ConnectAsync(string baseUrl, Func<Task<string?>> accessTokenProvider)
    {
        await DisconnectAsync();
        _hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/commands";
        _accessTokenProvider = accessTokenProvider;

        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl, options =>
            {
                options.AccessTokenProvider = () => accessTokenProvider();
            })
            .WithAutomaticReconnect()
            .Build();

        // Server sends a CommandMessage object (not a raw string).
        _connection.On<JsonElement>("ReceiveCommand", OnReceiveCommand);
        _connection.Reconnected += async _ =>
        {
            try { await _connection.InvokeAsync("SubscribeToCommands"); }
            catch { /* ignore */ }
            RefreshRequested?.Invoke("reconnected");
        };

        try
        {
            await _connection.StartAsync();
            try { await _connection.InvokeAsync("SubscribeToCommands"); }
            catch { /* older servers may not expose the method */ }
        }
        catch
        {
            await DisconnectAsync();
        }
    }

    private void OnReceiveCommand(JsonElement payload)
    {
        string? commandType = null;
        try
        {
            if (payload.ValueKind == JsonValueKind.String)
                commandType = payload.GetString();
            else if (payload.ValueKind == JsonValueKind.Object)
            {
                if (payload.TryGetProperty("commandType", out var ct) ||
                    payload.TryGetProperty("CommandType", out ct))
                    commandType = ct.GetString();
            }
        }
        catch { /* treat as generic refresh */ }

        RefreshRequested?.Invoke(commandType);
    }

    public async Task DisconnectAsync()
    {
        if (_connection == null) return;
        try
        {
            await _connection.StopAsync();
            await _connection.DisposeAsync();
        }
        catch { /* ignore */ }
        _connection = null;
    }

    public ValueTask DisposeAsync() => new(DisconnectAsync());
}
