namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Dedicated test accounts for messenger integration tests against production API.</summary>
internal static class MessengerTestCredentials
{
    public const string PrimaryLogin = "alex.plint@gmail.com";
    public static string PrimaryPassword =>
        Environment.GetEnvironmentVariable("PROTOLINK_TEST_PASSWORD")
        ?? throw new InvalidOperationException("Set PROTOLINK_TEST_PASSWORD for messenger integration tests.");
    public const string PartnerLogin = "molina.tina@ya.ru";
}
