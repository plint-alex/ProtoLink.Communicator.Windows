using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Navigation;
using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Offline helpers for Share sheet Add people (UX §4).</summary>
[Trait("Category", "Offline")]
public class ShareAddPeopleTests
{
    [Fact]
    public void ParseUserIdInput_AcceptsGuidAndPath()
    {
        var id = Guid.Parse("8f3a1111-2222-3333-4444-555555555555");
        Assert.Equal(id, ShareAddPeople.ParseUserIdInput(id.ToString("D")));
        Assert.Equal(
            id,
            ShareAddPeople.ParseUserIdInput($"https://protolink.ru/{id:D}?lang=ru-RU"));
        Assert.Null(ShareAddPeople.ParseUserIdInput("not-a-guid"));
        Assert.Null(ShareAddPeople.ParseUserIdInput(""));
    }

    [Fact]
    public void ClassifyPasteLookup_GuidUrlEmailAndGarbage()
    {
        var id = Guid.Parse("8f3a1111-2222-3333-4444-555555555555");
        Assert.Equal(ShareAddPeople.PasteLookupKind.Empty, ShareAddPeople.ClassifyPasteLookup("").Kind);
        Assert.Equal(ShareAddPeople.PasteLookupKind.Empty, ShareAddPeople.ClassifyPasteLookup("   ").Kind);

        var byId = ShareAddPeople.ClassifyPasteLookup(id.ToString("D"));
        Assert.Equal(ShareAddPeople.PasteLookupKind.ById, byId.Kind);
        Assert.Equal(id.ToString("D"), byId.Value);

        var fromUrl = ShareAddPeople.ClassifyPasteLookup($"https://protolink.ru/{id:D}?lang=ru-RU");
        Assert.Equal(ShareAddPeople.PasteLookupKind.ById, fromUrl.Kind);
        Assert.Equal(id.ToString("D"), fromUrl.Value);

        var email = ShareAddPeople.ClassifyPasteLookup("  molina.tina@ya.ru  ");
        Assert.Equal(ShareAddPeople.PasteLookupKind.ByLogin, email.Kind);
        Assert.Equal("molina.tina@ya.ru", email.Value);

        var garbage = ShareAddPeople.ClassifyPasteLookup("not-a-guid");
        Assert.Equal(ShareAddPeople.PasteLookupKind.ByLogin, garbage.Kind);
        Assert.True(ShareAddPeople.ShouldAutoResolvePaste("molina.tina@ya.ru"));
        Assert.True(ShareAddPeople.ShouldAutoResolvePaste(id.ToString("D")));
        Assert.False(ShareAddPeople.ShouldAutoResolvePaste(""));
    }

    [Fact]
    public void PickResolvedDisplayName_RejectsGuidShaped()
    {
        var id = "8f3a1111-2222-3333-4444-555555555555";
        Assert.Null(ShareAddPeople.PickResolvedDisplayName(id, id));
        Assert.Equal("Sergey L.", ShareAddPeople.PickResolvedDisplayName("Sergey L.", id));
        Assert.Equal("login", ShareAddPeople.PickResolvedDisplayName(null, "login"));
    }

    [Fact]
    public void ExtractContactUserId_FromValueOrCode()
    {
        var uid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var fromValue = new GetEntitiesResult
        {
            Code = "nick",
            Values = new List<EntityValue>
            {
                new() { Type = "StringValue", Value = $"userid:{uid:D}" }
            }
        };
        Assert.Equal(uid, ShareAddPeople.ExtractContactUserId(fromValue));

        var fromCode = new GetEntitiesResult { Code = uid.ToString("D") };
        Assert.Equal(uid, ShareAddPeople.ExtractContactUserId(fromCode));
    }

    [Fact]
    public void FilterContacts_IsClientSideCaseInsensitive()
    {
        var rows = new[]
        {
            (Id: 1, Name: "Maria K."),
            (Id: 2, Name: "Pavel D."),
            (Id: 3, Name: "Sergey L.")
        };
        var filtered = ShareAddPeople.FilterContacts(rows, r => r.Name, "pav");
        Assert.Single(filtered);
        Assert.Equal("Pavel D.", filtered[0].Name);
        Assert.Equal(3, ShareAddPeople.FilterContacts(rows, r => r.Name, null).Count);
    }

    [Fact]
    public void IsPeopleListPrincipal_ExcludesGuestLink()
    {
        Assert.True(ShareAddPeople.IsPeopleListPrincipal("user"));
        Assert.True(ShareAddPeople.IsPeopleListPrincipal("public"));
        Assert.False(ShareAddPeople.IsPeopleListPrincipal("guest_link"));
        Assert.False(ShareAddPeople.IsPeopleListPrincipal("Guest_Link"));
    }

    [Fact]
    public void AddPeopleStrings_EnglishAndRussian()
    {
        Assert.Equal("+ Add people", ShareSheetStrings.AddPeopleButton("en"));
        Assert.Equal("+ Добавить людей", ShareSheetStrings.AddPeopleButton("ru-RU"));
        Assert.Contains("internet", ShareSheetStrings.AnyoneOnInternet("en"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("интернет", ShareSheetStrings.AnyoneOnInternet("ru"), StringComparison.OrdinalIgnoreCase);
        var toast = ShareSheetStrings.AddedToast("en", "Maria K.", canWrite: false);
        Assert.Contains("Maria K.", toast, StringComparison.Ordinal);
        Assert.Contains("view", toast, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", ShareSheetStrings.PasteIdHint("en"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("email", ShareSheetStrings.PasteIdHint("en"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("email", ShareSheetStrings.NoUserWithId("en"), StringComparison.OrdinalIgnoreCase);
    }
}
