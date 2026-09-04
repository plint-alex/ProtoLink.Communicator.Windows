using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;
using Xunit;
using Xunit.Abstractions;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// API regression: NAME_TYPE must survive AddFile version bump.
/// When broken, GetEntities shows synthetic file-{guid} (the Films trash pattern).
/// </summary>
public class CloudFileNameAfterUploadIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public CloudFileNameAfterUploadIntegrationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task AddEntityWithName_ThenAddFile_DisplayNameRemainsIndexHtml()
    {
        var password = Environment.GetEnvironmentVariable("PROTOLINK_TEST_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            _output.WriteLine("Skip: set PROTOLINK_TEST_PASSWORD");
            return;
        }

        var login = Environment.GetEnvironmentVariable("PROTOLINK_TEST_LOGIN") ?? MessengerTestCredentials.PrimaryLogin;
        using var http = new HttpClient();
        IntegrationTestHttp.ConfigureClient(http);

        var loginResp = await http.PostAsJsonAsync("api/Authentication/login", new { login, password });
        var loginBody = await IntegrationTestHttp.AssertSuccessAsync(loginResp, "login");
        using var loginDoc = JsonDocument.Parse(loginBody);
        var token = loginDoc.RootElement.TryGetProperty("accessToken", out var at)
            ? at.GetString()
            : loginDoc.RootElement.GetProperty("token").GetString();
        var userId = loginDoc.RootElement.TryGetProperty("userId", out var uid)
            ? uid.GetGuid()
            : loginDoc.RootElement.GetProperty("id").GetGuid();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var api = new CloudApiService(http);
        Guid? folderId = null;
        try
        {
            var roots = await api.GetEntitiesAsync(new[] { userId }, includeValues: false);
            var cloudRoot = roots.FirstOrDefault(e => e.Code == CloudEntityCodes.CloudRoot)?.Id
                            ?? await api.AddEntityAsync(CloudEntityCodes.CloudRoot, new[] { userId });

            var folderName = "name-reg-" + Guid.NewGuid().ToString("N")[..8];
            folderId = await api.AddEntityAsync(CloudEntityCodes.CloudFolder, new[] { cloudRoot }, folderName);

            const string fileName = "index.html";
            var fileId = await api.AddEntityAsync(CloudEntityCodes.CloudFile, new[] { folderId.Value }, fileName);

            // Confirm name present before upload
            var before = (await api.GetEntitiesAsync(new[] { folderId.Value }, includeValues: true))
                .Single(e => e.Id == fileId);
            var nameBefore = CloudApiService.GetDisplayName(before);
            Assert.Equal(fileName, nameBefore, ignoreCase: true);
            Assert.False(CloudApiService.IsSyntheticCloudFileDisplayName(before, nameBefore));

            await using (var ms = new MemoryStream(Encoding.UTF8.GetBytes("<h1>films</h1>\n\n<p><br></p>")))
            {
                // Call AddFile via raw HTTP — do NOT use CloudApiService.AddFileAsync, which
                // re-asserts NAME_TYPE as a client workaround for the live-server bug.
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent(fileId.ToString()), "EntityId");
                var streamContent = new StreamContent(ms);
                streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");
                form.Add(streamContent, "File", fileName);
                var upload = await http.PostAsync("api/Files/addFile", form);
                Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync());
            }

            var after = (await api.GetEntitiesAsync(new[] { folderId.Value }, includeValues: true))
                .Single(e => e.Id == fileId);
            var nameAfter = CloudApiService.GetDisplayName(after);
            var isSynthetic = CloudApiService.IsSyntheticCloudFileDisplayName(after, nameAfter);

            _output.WriteLine($"Before AddFile: '{nameBefore}'");
            _output.WriteLine($"After AddFile:  '{nameAfter}' synthetic={isSynthetic} version={after.Version}");
            if (after.Values != null)
            {
                foreach (var v in after.Values)
                {
                    var parents = v.Parents == null ? "" : string.Join(",", v.Parents);
                    _output.WriteLine($"  value type={v.Type} parents=[{parents}] raw={v.Value}");
                }
            }

            // Correct behavior: name must survive AddFile. Failure here is the root cause of Films trash.
            Assert.False(isSynthetic,
                $"AddFile dropped NAME_TYPE; display became '{nameAfter}'. " +
                "Sync then skips this remote and re-uploads duplicates.");
            Assert.Equal(fileName, nameAfter, ignoreCase: true);
        }
        finally
        {
            if (folderId is { } id)
            {
                try
                {
                    var children = await api.GetEntitiesAsync(new[] { id }, includeValues: false);
                    foreach (var c in children)
                        await api.DeleteEntityAsync(c.Id);
                    await api.DeleteEntityAsync(id);
                }
                catch (Exception ex)
                {
                    _output.WriteLine("Cleanup failed: " + ex.Message);
                }
            }
        }
    }
}
