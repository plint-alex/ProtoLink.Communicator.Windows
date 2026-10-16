using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class LiveApiTargetTests
{
    [Fact]
    public void NoBase_Skips_AndDoesNotNameProductionAsDefault()
    {
        var reason = LiveApiTarget.SkipReason(null, null);
        Assert.Equal(LiveApiTarget.NoBaseSkip, reason);
        Assert.DoesNotContain("default", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("localhost:5000", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Localhost_IsAllowed_WithoutProdOptIn()
    {
        Assert.Null(LiveApiTarget.SkipReason("http://localhost:5000", null));
        Assert.Null(LiveApiTarget.SkipReason("http://127.0.0.1:5000/", "0"));
        Assert.True(LiveApiTarget.TryNormalize("http://localhost:5000", out var url, out _));
        Assert.Equal("http://localhost:5000/", url);
    }

    [Theory]
    [InlineData("https://protolink.ru")]
    [InlineData("http://protolink.ru/")]
    [InlineData("https://www.protolink.ru/api")]
    [InlineData("https://API.ProtoLink.ru")]
    [InlineData("https://protolink.ru.")]
    public void ProductionHost_RequiresExplicitOptIn(string apiBase)
    {
        Assert.Equal(LiveApiTarget.ProductionSkip, LiveApiTarget.SkipReason(apiBase, null));
        Assert.Equal(LiveApiTarget.ProductionSkip, LiveApiTarget.SkipReason(apiBase, "true"));
        Assert.Equal(LiveApiTarget.ProductionSkip, LiveApiTarget.SkipReason(apiBase, "yes"));
        Assert.Null(LiveApiTarget.SkipReason(apiBase, "1"));
    }

    [Fact]
    public void UnrelatedHost_IsNotTreatedAsProduction()
    {
        Assert.Null(LiveApiTarget.SkipReason("https://protolink.ru.example", null));
        Assert.False(LiveApiTarget.IsProductionHost(new Uri("https://notprotolink.ru")));
    }

    [Fact]
    public void RelativeOrNonHttp_IsRejected()
    {
        Assert.Equal(LiveApiTarget.InvalidBaseSkip, LiveApiTarget.SkipReason("/api", null));
        Assert.Equal(LiveApiTarget.InvalidBaseSkip, LiveApiTarget.SkipReason("ftp://protolink.ru", null));
    }
}
