using Amazon.S3;
using Amazon.S3.Model;

// MinIO/S3 helpers for publish (object key = CloudFile entity id, FilesService convention).
// Usage:
//   CloudBlobUpload --key <guid> --file <path> [--content-type ...]   # upload (default)
//   CloudBlobUpload --key <destGuid> --copy-from <sourceGuid> [...]   # server-side copy
//   CloudBlobUpload --key <guid> --delete [...]                       # delete object

static string? Arg(string name)
{
    var args = Environment.GetCommandLineArgs();
    for (var i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    return null;
}

static bool HasFlag(string name) =>
    Environment.GetCommandLineArgs().Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

var key = Arg("--key") ?? throw new ArgumentException("--key required");
var copyFrom = Arg("--copy-from");
var file = Arg("--file");
var delete = HasFlag("--delete");

var endpoint = Arg("--endpoint") ?? "plintec.ru:9000";
var accessKey = Arg("--access-key") ?? "minioadmin";
var secretKey = Arg("--secret-key") ?? "minioadmin";
var bucket = Arg("--bucket") ?? "protolink-files";
var contentType = Arg("--content-type") ?? "application/octet-stream";
var useSsl = HasFlag("--ssl");

var modeCount = (file != null ? 1 : 0) + (copyFrom != null ? 1 : 0) + (delete ? 1 : 0);
if (modeCount != 1)
{
    Console.Error.WriteLine("Specify exactly one of: --file, --copy-from, or --delete");
    return 1;
}

var config = new AmazonS3Config
{
    ServiceURL = $"{(useSsl ? "https" : "http")}://{endpoint}",
    ForcePathStyle = true,
    AuthenticationRegion = "us-east-1"
};

using var s3 = new AmazonS3Client(accessKey, secretKey, config);

if (delete)
{
    await s3.DeleteObjectAsync(new DeleteObjectRequest
    {
        BucketName = bucket,
        Key = key
    });
    Console.WriteLine($"Deleted s3://{bucket}/{key}");
    return 0;
}

if (copyFrom != null)
{
    await s3.CopyObjectAsync(new CopyObjectRequest
    {
        SourceBucket = bucket,
        SourceKey = copyFrom,
        DestinationBucket = bucket,
        DestinationKey = key,
        ContentType = contentType
    });
    Console.WriteLine($"Copied s3://{bucket}/{copyFrom} -> s3://{bucket}/{key}");
    return 0;
}

if (!File.Exists(file!))
{
    Console.Error.WriteLine($"File not found: {file}");
    return 1;
}

await using var fs = File.OpenRead(file!);
await s3.PutObjectAsync(new PutObjectRequest
{
    BucketName = bucket,
    Key = key,
    InputStream = fs,
    ContentType = contentType
});

Console.WriteLine($"Uploaded {file} -> s3://{bucket}/{key} ({new FileInfo(file!).Length} bytes)");
return 0;
