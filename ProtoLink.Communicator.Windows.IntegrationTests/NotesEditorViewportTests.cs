using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>
/// Offline coverage for Notes same-page refresh viewport preserve (scroll + caret offsets).
/// </summary>
[Trait("Category", "Offline")]
public class NotesEditorViewportTests
{
    [Fact]
    public void CaptureScript_ReadsScrollAndCaretOffsets()
    {
        var script = NotesEditorViewport.BuildCaptureScript();
        Assert.Contains("scrollY", script);
        Assert.Contains("caretStart", script);
        Assert.Contains("getElementById('editor')", script);
        Assert.Contains("offsetInEditor", script);
    }

    [Fact]
    public void ApplyInnerHtmlScript_EmbedsHtmlAndRestoresViewport()
    {
        var state = new NotesEditorViewport.State(420.5, 12, 18);
        var script = NotesEditorViewport.BuildApplyInnerHtmlScript("<p>hello &amp; world</p>", state);

        Assert.Contains("getElementById('editor')", script);
        Assert.Contains("editor.innerHTML=", script);
        Assert.Contains("hello &amp; world", script);
        Assert.Contains("420.5", script);
        Assert.Contains("caretStart=12", script);
        Assert.Contains("caretEnd=18", script);
        Assert.Contains("window.scrollTo", script);
        Assert.Contains("createTreeWalker", script);
    }

    [Fact]
    public void ApplyInnerHtmlScript_EscapesQuotesInHtml()
    {
        var script = NotesEditorViewport.BuildApplyInnerHtmlScript("<p onclick=\"x\">a</p>", viewport: null);
        Assert.Contains("onclick=\\\"x\\\"", script);
        Assert.Contains("caretStart=null", script);
    }

    [Fact]
    public void RestoreScript_AppliesScrollAndCaret()
    {
        var script = NotesEditorViewport.BuildRestoreScript(new NotesEditorViewport.State(88, 3, 3));
        Assert.Contains("88", script);
        Assert.Contains("caretStart=3", script);
        Assert.Contains("window.scrollTo", script);
    }

    [Fact]
    public void ParseCaptureResult_PlainJsonObject()
    {
        var state = NotesEditorViewport.ParseCaptureResult(
            """{"scrollY":100.5,"caretStart":10,"caretEnd":15}""");
        Assert.NotNull(state);
        Assert.Equal(100.5, state!.ScrollY);
        Assert.Equal(10, state.CaretStart);
        Assert.Equal(15, state.CaretEnd);
    }

    [Fact]
    public void ParseCaptureResult_JsonEncodedStringFromWebView2()
    {
        // CoreWebView2 wraps a returned string as a JSON string literal.
        var raw = "\"{\\\"scrollY\\\":50,\\\"caretStart\\\":null,\\\"caretEnd\\\":null}\"";
        var state = NotesEditorViewport.ParseCaptureResult(raw);
        Assert.NotNull(state);
        Assert.Equal(50, state!.ScrollY);
        Assert.Null(state.CaretStart);
        Assert.Null(state.CaretEnd);
    }

    [Fact]
    public void ParseCaptureResult_NullLiteral()
    {
        Assert.Null(NotesEditorViewport.ParseCaptureResult("null"));
    }
}
