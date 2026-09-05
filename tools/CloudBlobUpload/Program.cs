using Amazon.S3;
using Amazon.S3.Model;

// Uploads a file to MinIO/S3 with object key = entity id (FilesService convention).
// Usage: CloudBlobUpload --key <guid> --file <path> [--content-type ...] [--endpoint ...] ...

static string? Arg(string name)
{
    var args = Environment.GetCommandLineArgs();
    for (var i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    return null;
}

var key = Arg("--key") ?? throw new ArgumentException("--key required");
var file = Arg("--file") ?? throw new ArgumentException("--file required");
if (!File.Exists(file))
{
    Console.Error.WriteLine($"File not found: {file}");
    return 1;
}

var endpoint = Arg("--endpoint") ?? "plintec.ru:9000";
var accessKey = Arg("--access-key") ?? "minioadmin";
var secretKey = Arg("--secret-key") ?? "minioadmin";
var bucket = Arg("--bucket") ?? "protolink-files";
var contentType = Arg("--content-type") ?? "application/octet-stream";
var useSsl = Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--ssl", StringComparison.OrdinalIgnoreCase));

var config = new AmazonS3Config
{
    ServiceURL = $"{(useSsl ? "https" : "http")}://{endpoint}",
    ForcePathStyle = true,
    AuthenticationRegion = "us-east-1"
};

using var s3 = new AmazonS3Client(accessKey, secretKey, config);
await using var fs = File.OpenRead(file);
await s3.PutObjectAsync(new PutObjectRequest
{
    BucketName = bucket,
    Key = key,
    InputStream = fs,
    ContentType = contentType
});

Console.WriteLine($"Uploaded {file} -> s3://{bucket}/{key} ({new FileInfo(file).Length} bytes)");
return 0;
