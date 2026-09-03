using Microsoft.AspNetCore.SignalR.Client;

namespace ProtoLink.Communicator.Windows.Services;

public class SignalRService
{
    private HubConnection? _connection;
    private readonly string _hubUrl;
    private readonly string _accessToken;

    public event Action? MessageReceived;

    public SignalRService(string baseUrl, string accessToken)
    {
        _hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/commands";
        _accessToken = accessToken;
    }

    public async Task ConnectAsync()
    {
        try
        {
            _connection = new HubConnectionBuilder()
                .WithUrl(_hubUrl, options => options.AccessTokenProvider = () => Task.FromResult<string?>(_accessToken))
                .WithAutomaticReconnect()
                .Build();

            _connection.On<string>("ReceiveCommand", _ => MessageReceived?.Invoke());
            await _connection.StartAsync();
        }
        catch
        {
            _connection = null;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_connection != null)
        {
            await _connection.StopAsync();
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
