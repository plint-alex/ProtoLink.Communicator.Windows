namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Dedicated test accounts (emails only). Password from <c>PROTOLINK_TEST_PASSWORD</c>.
/// Host from <c>PROTOLINK_API_BASE</c>, not a production default.
/// Account map (Agents docs/ops/environment-map § test accounts):
/// custom page = <see cref="PrimaryLogin"/> (gmail); default page = <see cref="DefaultPageLogin"/> (yandex).
/// Override login with <c>PROTOLINK_TEST_LOGIN</c> when a named test needs a different account.
/// </summary>
internal static class MessengerTestCredentials
{
    /// <summary>Custom-page / fixture-rich primary. Most live tests use this unless they need default-page.</summary>
    public const string PrimaryLogin = "alex.plint@gmail.com";

    /// <summary>Default-page login. Use when a named test needs default view / default-page UX.</summary>
    public const string DefaultPageLogin = "alex.plint@yandex.ru";

    public static string PrimaryPassword =>
        Environment.GetEnvironmentVariable("PROTOLINK_TEST_PASSWORD")
        ?? throw new InvalidOperationException("Set PROTOLINK_TEST_PASSWORD for messenger integration tests.");

    public const string PartnerLogin = "molina.tina@ya.ru";
}
