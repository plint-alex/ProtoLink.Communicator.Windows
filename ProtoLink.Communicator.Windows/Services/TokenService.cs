using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Services;

public class TokenService : ITokenService
{
    private readonly string _filePath;
    private readonly ILogger<TokenService> _logger;

    public TokenService(ILogger<TokenService> logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "ProtoLinkCommunicator");
        Directory.CreateDirectory(appFolder);
        _filePath = Path.Combine(appFolder, "token.dat");
    }

    public void SaveToken(TokenData data)
    {
        var json = JsonConvert.SerializeObject(data);
        var plainBytes = Encoding.UTF8.GetBytes(json);
        var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_filePath, encryptedBytes);
    }

    public TokenData? LoadToken()
    {
        if (!File.Exists(_filePath)) return null;
        try
        {
            var encryptedBytes = File.ReadAllBytes(_filePath);
            var plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plainBytes);
            return JsonConvert.DeserializeObject<TokenData>(json);
        }
        catch
        {
            return null;
        }
    }

    public void ClearToken()
    {
        if (File.Exists(_filePath)) File.Delete(_filePath);
    }
}
