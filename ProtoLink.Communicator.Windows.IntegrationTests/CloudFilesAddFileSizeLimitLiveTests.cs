using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Live AT-M30 (server): just-over 100 MiB → product 413 <c>file_too_large</c>;
/// just-under → success. Requires LocalStand (or equiv) with Api <c>b7f5832</c>+,
/// <c>PROTOLINK_API_BASE</c>, <c>PROTOLINK_TEST_PASSWORD</c>.
/// </summary>
[Trait("Category", "Live")]
public sealed class CloudFilesAddFileSizeLimitLiveTests
{
    private const long LimitBytes = 104857600; // Files:MaxUploadBytes default
    private const long JustUnderBytes = LimitBytes - 4096; // leave room for multipart framing
    private const long JustOverBytes = LimitBytes + 1;

    private static readonly Guid NameTypeId = Guid.Parse("00010003-0000-0000-0000-000000000000");

    private readonly ITestOutputHelper _output;

    public CloudFilesAddFileSizeLimitLiveTests(ITestOutputHelper output) => _output = output;

    [LiveFact]
    public async Task AddFile_JustOver_ReturnsProduct413_JustUnder_StoresBlob()
    {
        var password = Environment.GetEnvironmentVariable("PROTOLINK_TEST_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            _output.WriteLine("Skip: set PROTOLINK_TEST_PASSWORD");
            return;
        }

        var ownerLogin = Environment.GetEnvironmentVariable("PROTOLINK_TEST_LOGIN")
                         ?? MessengerTestCredentials.PrimaryLogin;

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        IntegrationTestHttp.ConfigureClient(http);

        var owner = await LoginAsync(http, ownerLogin, password);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);

        Guid? folderId = null;
        Guid? overFileId = null;
        Guid? underFileId = null;
        try
        {
            var cloudRoot = await EnsureCloudRootAsync(http, owner.UserId);
            var folderName = "addfile-size-" + Guid.NewGuid().ToString("N")[..8];
            folderId = await AddNamedEntityAsync(http, "CloudFolder", cloudRoot, folderName);

            overFileId = await AddNamedEntityAsync(http, "CloudFile", folderId.Value, "over.bin");
            // curl (Content-Length) matches browser/typical clients. HttpClient often keeps writing the
            // body after an early 413 and aborts the socket before the JSON can be read.
            var (overStatus, overBody) = await CurlAddFileAsync(
                http.BaseAddress!,
                owner.Token,
                overFileId.Value,
                "over.bin",
                JustOverBytes,
                fillByte: 0xAB);
            _output.WriteLine($"just-over status={overStatus} body={Trim(overBody, 400)}");
            Assert.Equal(413, overStatus);
            using (var overDoc = JsonDocument.Parse(overBody))
            {
                Assert.True(overDoc.RootElement.TryGetProperty("code", out var code));
                Assert.Equal("file_too_large", code.GetString());
                Assert.True(overDoc.RootElement.TryGetProperty("limitBytes", out var limit));
                Assert.Equal(LimitBytes, limit.GetInt64());
            }

            // Oversize must not leave a stored blob.
            var afterOver = await http.GetAsync($"api/Files/getFile/{overFileId.Value:D}");
            Assert.NotEqual(HttpStatusCode.OK, afterOver.StatusCode);

            underFileId = await AddNamedEntityAsync(http, "CloudFile", folderId.Value, "under.bin");
            using (var underForm = BuildSizedMultipart(underFileId.Value, "under.bin", JustUnderBytes, fillByte: 0x5A))
            {
                ForceContentLength(underForm);
                var under = await http.PostAsync("api/Files/addFile", underForm);
                var underBody = await under.Content.ReadAsStringAsync();
                _output.WriteLine($"just-under status={(int)under.StatusCode} body={Trim(underBody, 200)}");
                Assert.True(under.IsSuccessStatusCode, underBody);
                using var underDoc = JsonDocument.Parse(underBody);
                Assert.True(
                    underDoc.RootElement.TryGetProperty("success", out var ok)
                    || underDoc.RootElement.TryGetProperty("Success", out ok));
                Assert.Equal("ok", ok.GetString());
            }

            var stored = await http.GetAsync($"api/Files/getFile/{underFileId.Value:D}");
            Assert.Equal(HttpStatusCode.OK, stored.StatusCode);
            await using var storedStream = await stored.Content.ReadAsStreamAsync();
            var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buf = new byte[1024 * 1024];
            long total = 0;
            int read;
            var first = new byte[64];
            var firstFilled = false;
            while ((read = await storedStream.ReadAsync(buf)) > 0)
            {
                if (!firstFilled)
                {
                    var n = Math.Min(64, read);
                    buf.AsSpan(0, n).CopyTo(first);
                    firstFilled = true;
                }

                hasher.AppendData(buf, 0, read);
                total += read;
            }

            Assert.Equal(JustUnderBytes, total);
            Assert.All(first, b => Assert.Equal((byte)0x5A, b));
            var underSha = hasher.GetHashAndReset();
            _output.WriteLine(
                $"addFile size-limit live OK underBytes={JustUnderBytes} sha256={Convert.ToHexString(underSha)}");
        }
        finally
        {
            await BestEffortDeleteAsync(http, underFileId);
            await BestEffortDeleteAsync(http, overFileId);
            await BestEffortDeleteAsync(http, folderId);
        }
    }

    private static MultipartFormDataContent BuildSizedMultipart(
        Guid entityId,
        string fileName,
        long fileBytes,
        byte fillByte)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(entityId.ToString("D")), "EntityId");
        var part = new StreamContent(new ConstantByteStream(fileBytes, fillByte));
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        part.Headers.ContentLength = fileBytes;
        form.Add(part, "File", fileName);
        return form;
    }

    private void ForceContentLength(MultipartFormDataContent form)
    {
        var cl = form.Headers.ContentLength;
        if (cl is null or <= 0)
            throw new InvalidOperationException("Multipart Content-Length was not computed; check stream CanSeek.");
        _output.WriteLine("multipart Content-Length=" + cl);
    }

    private static async Task<(int Status, string Body)> CurlAddFileAsync(
        Uri baseAddress,
        string bearer,
        Guid entityId,
        string fileName,
        long fileBytes,
        byte fillByte)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "pl-size-" + Guid.NewGuid().ToString("N") + ".bin");
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024,
                         FileOptions.SequentialScan))
        {
            var buf = new byte[1024 * 1024];
            Array.Fill(buf, fillByte);
            var left = fileBytes;
            while (left > 0)
            {
                var n = (int)Math.Min(left, buf.Length);
                await fs.WriteAsync(buf.AsMemory(0, n));
                left -= n;
            }
        }

        try
        {
            var url = new Uri(baseAddress, "api/Files/addFile").AbsoluteUri;
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "curl.exe",
                ArgumentList =
                {
                    "-s", "-S", "-w", "\nHTTP_CODE:%{http_code}",
                    "--max-time", "120",
                    "-H", "Authorization: Bearer " + bearer,
                    "-F", "EntityId=" + entityId.ToString("D"),
                    "-F", $"File=@{tmp};filename={fileName};type=application/octet-stream",
                    url,
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi)
                             ?? throw new InvalidOperationException("Failed to start curl.exe");
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            Assert.True(proc.ExitCode == 0, "curl failed: " + stderr + "\n" + stdout);
            const string marker = "\nHTTP_CODE:";
            var idx = stdout.LastIndexOf(marker, StringComparison.Ordinal);
            Assert.True(idx >= 0, "curl missing HTTP_CODE: " + stdout);
            var body = stdout[..idx];
            var status = int.Parse(stdout[(idx + marker.Length)..].Trim());
            return (status, body);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    private static string Trim(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private static async Task BestEffortDeleteAsync(HttpClient http, Guid? id)
    {
        if (id is not { } entityId)
            return;
        try
        {
            await http.PostAsJsonAsync("api/Entities/DeleteEntity", new { id = entityId });
        }
        catch
        {
            /* best-effort cleanup */
        }
    }

    private static async Task<(string Token, Guid UserId)> LoginAsync(
        HttpClient http,
        string login,
        string password)
    {
        var loginResp = await http.PostAsJsonAsync("api/Authentication/login", new { login, password });
        var loginBody = await IntegrationTestHttp.AssertSuccessAsync(loginResp, "login " + login);
        using var loginDoc = JsonDocument.Parse(loginBody);
        var token = loginDoc.RootElement.TryGetProperty("accessToken", out var at)
            ? at.GetString()
            : loginDoc.RootElement.GetProperty("token").GetString();
        var userId = loginDoc.RootElement.TryGetProperty("userId", out var uid)
            ? uid.GetGuid()
            : loginDoc.RootElement.GetProperty("id").GetGuid();
        Assert.False(string.IsNullOrEmpty(token));
        return (token!, userId);
    }

    private static async Task<Guid> EnsureCloudRootAsync(HttpClient http, Guid userId)
    {
        var roots = await http.PostAsJsonAsync("api/Entities/GetEntities", new
        {
            parentIds = new[] { userId },
            includeValues = false,
            take = 200
        });
        var rootsJson = await IntegrationTestHttp.AssertSuccessAsync(roots, "GetEntities cloud root");
        using var rootsDoc = JsonDocument.Parse(rootsJson);
        var found = rootsDoc.RootElement.EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("code").GetString() == "CloudRoot");
        if (found.ValueKind == JsonValueKind.Object)
            return found.GetProperty("id").GetGuid();

        var addRoot = await http.PostAsJsonAsync("api/Entities/AddEntity", new
        {
            code = "CloudRoot",
            parentIds = new[] { userId }
        });
        var addRootBody = await IntegrationTestHttp.AssertSuccessAsync(addRoot, "AddEntity CloudRoot");
        return JsonDocument.Parse(addRootBody).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> AddNamedEntityAsync(
        HttpClient http,
        string code,
        Guid parentId,
        string name)
    {
        var add = await http.PostAsJsonAsync("api/Entities/AddEntity", new
        {
            code,
            parentIds = new[] { parentId },
            values = new[]
            {
                new { type = 0, value = name, parentIds = new[] { NameTypeId } }
            }
        });
        var body = await IntegrationTestHttp.AssertSuccessAsync(add, "AddEntity " + code);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Streams a fixed byte without allocating the full buffer.</summary>
    private sealed class ConstantByteStream : Stream
    {
        private readonly long _length;
        private readonly byte _fill;
        private long _position;

        public ConstantByteStream(long length, byte fill)
        {
            _length = length;
            _fill = fill;
        }

        public override bool CanRead => true;
        // Seekable so StreamContent/MultipartFormDataContent emit Content-Length (not chunked).
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > _length)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = _length - _position;
            if (remaining <= 0)
                return 0;
            var n = (int)Math.Min(count, remaining);
            buffer.AsSpan(offset, n).Fill(_fill);
            _position += n;
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = _length - _position;
            if (remaining <= 0)
                return ValueTask.FromResult(0);
            var n = (int)Math.Min(buffer.Length, remaining);
            buffer.Span[..n].Fill(_fill);
            _position += n;
            return ValueTask.FromResult(n);
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var next = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            Position = next;
            return _position;
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
