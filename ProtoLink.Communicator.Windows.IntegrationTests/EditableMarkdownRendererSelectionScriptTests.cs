using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

[Trait("Category", "Offline")]
public class EditableMarkdownRendererSelectionScriptTests
{
    [Fact]
    public void CreateEditableHtml_IncludesSelectionChangeReporting()
    {
        var html = new EditableMarkdownRenderer().CreateEditableHtml("<p>hi</p>");

        Assert.Contains("selectionChanged", html);
        Assert.Contains("dismissFormatBar", html);
        Assert.Contains("selectionchange", html);
        Assert.Contains("hasSelection", html);
        Assert.Contains("window._noteSelT", html);
    }
}
