using System.Reflection;
using System.Text.RegularExpressions;
using ProtoLink.Communicator.Windows.Utilities;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

public class NotesEditorShellTests
{
    [Fact]
    public void CreateEditableHtml_embeds_shared_shell_and_editor_div()
    {
        var renderer = new EditableMarkdownRenderer();
        var doc = renderer.CreateEditableHtml("<p>Hi</p>");

        Assert.Contains("id=\"editor\"", doc);
        Assert.Contains("contenteditable=\"true\"", doc);
        Assert.Contains("data-content-base64=", doc);
        Assert.Contains("window.notesEditor", doc);
        Assert.Contains("window.getHtml", doc);
        Assert.DoesNotContain("outline:2px solid #0066cc", doc);
    }

    [Fact]
    public void CreateEditableHtml_encodes_inner_html_as_utf8_base64()
    {
        var renderer = new EditableMarkdownRenderer();
        var inner = "<p>Привет</p>";
        var doc = renderer.CreateEditableHtml(inner);
        var m = Regex.Match(doc, "data-content-base64=\"([^\"]+)\"");
        Assert.True(m.Success);
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value));
        Assert.Contains("Привет", decoded);
    }

    [Fact]
    public void Assembly_embeds_notes_editor_js_resource()
    {
        var asm = typeof(EditableMarkdownRenderer).Assembly;
        using var stream = asm.GetManifestResourceStream("ProtoLink.NotesEditor.notes-editor.js");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var js = reader.ReadToEnd();
        Assert.Contains("sanitizeHtml", js);
        Assert.Contains("checkbox-list", js);
        Assert.Contains("requestLink", js);
    }
}
