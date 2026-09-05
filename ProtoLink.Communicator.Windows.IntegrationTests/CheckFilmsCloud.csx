using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ProtoLink.Communicator.Windows.Services;
using ProtoLink.Communicator.Windows.Services.Sync;

var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ""ProtoLinkCommunicator"");
var tokenService = new TokenService(Microsoft.Extensions.Logging.Abstractions.NullLogger<TokenService>.Instance);
var token = tokenService.LoadToken();
if (token == null) { Console.WriteLine(""NO_TOKEN""); return; }
Console.WriteLine(""login="" + token.Login);
using var http = new System.Net.Http.HttpClient { BaseAddress = new Uri(""https://protolink.ru/"") };
http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(""Bearer"", token.AccessToken);
var api = new CloudApiService(http);
var id = Guid.Parse(""bc68252d-d15d-467c-abc0-cf258824721e"");
await using var stream = await api.GetFileStreamOrNotFoundAsync(id);
if (stream == null) { Console.WriteLine(""NO_FILE""); return; }
using var ms = new MemoryStream();
await stream.CopyToAsync(ms);
var bytes = ms.ToArray();
var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
var text = System.Text.Encoding.UTF8.GetString(bytes);
Console.WriteLine($""cloud size={bytes.Length} hash={hash}"");
Console.WriteLine(text.Contains(""Test8"") ? ""HAS_Test8"" : text.Contains(""Test7"") ? ""HAS_Test7"" : ""OTHER"");
Console.WriteLine(text.Length > 200 ? text[^120..] : text);
