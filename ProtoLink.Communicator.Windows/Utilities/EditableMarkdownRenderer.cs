using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace ProtoLink.Communicator.Windows.Utilities;

public class EditableMarkdownRenderer
{
    private static readonly Lazy<string> EditorScript = new(LoadEditorScript);

    public string CreateEditableHtml(string htmlContent)
    {
        var html = htmlContent ?? string.Empty;
        html = CleanupEscapedContent(html);
        html = html.Replace("\\n", "\n").Replace("\\r", "\r");
        html = Regex.Replace(html, @"\r?\n", " ");
        if (string.IsNullOrWhiteSpace(html)) html = "<p><br></p>";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
        const string extraStyles = """
ul.checkbox-list{list-style:none;padding-left:0;}
ul.checkbox-list li{padding:4px 0;display:flex;align-items:flex-start;}
ul.checkbox-list li input[type="checkbox"]{margin-right:8px;margin-top:2px;cursor:pointer;flex-shrink:0;width:16px;height:16px;}
ul.checkbox-list li label{cursor:text;flex:1;margin:0;}
ul.checkbox-list li:has(input:checked){text-decoration:line-through;opacity:0.6;}
table{border-collapse:collapse;margin:0.5em 0;}
td,th{border:1px solid #dfe1e6;padding:6px;}
pre,code{font-family:Consolas,'Courier New',monospace;background:#f4f5f7;border-radius:3px;}
pre{padding:8px 12px;overflow:auto;}
code{padding:1px 4px;}
""";
        var script = EditorScript.Value;
        var doc = $$"""
<!DOCTYPE html>
<html>
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1.0">
<style>
html{color-scheme:light;background:#ffffff;}
html,body{height:100%;margin:0;background:#ffffff;color:#111111;}
body{font-family:'Segoe UI',sans-serif;line-height:1.6;padding:12px 16px;min-height:100%;box-sizing:border-box;}
#editor{outline:none;min-height:100%;font-size:15px;padding:8px 4px 48px;box-sizing:border-box;background:#ffffff;color:#111111;}
#editor:focus{outline:none;}
h1,h2,h3{margin-top:1em;margin-bottom:0.5em;}
p{margin:0.5em 0;}
ul,ol{padding-left:2em;}
/*__NOTE_EXTRA_STYLES__*/
a{color:#0366d6;text-decoration:none;cursor:default;}
a:hover{text-decoration:underline;}
body[data-ctrl-key="true"] a{cursor:pointer;}
</style>
</head>
<body>
<div id="editor" contenteditable="true" data-content-base64="{{base64}}"></div>
<script>
{{script}}
</script>
</body>
</html>
""";
        return doc.Replace("/*__NOTE_EXTRA_STYLES__*/", extraStyles.Trim(), StringComparison.Ordinal);
    }

    private static string LoadEditorScript()
    {
        var asm = Assembly.GetExecutingAssembly();
        const string name = "ProtoLink.NotesEditor.notes-editor.js";
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing embedded resource: " + name);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string CleanupEscapedContent(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;
        var r = content;
        r = Regex.Replace(r, @"&quot;+", "\"");
        r = Regex.Replace(r, @"&amp;+", "&");
        r = Regex.Replace(r, @"&lt;+", "<");
        r = Regex.Replace(r, @"&gt;+", ">");
        return r;
    }
}
