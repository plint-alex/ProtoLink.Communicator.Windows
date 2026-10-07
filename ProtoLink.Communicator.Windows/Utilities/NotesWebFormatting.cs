using Microsoft.Web.WebView2.Core;

namespace ProtoLink.Communicator.Windows.Utilities;

public static class NotesWebFormatting
{
    public static Task ApplyEditorApiAsync(CoreWebView2 core, string command)
    {
        var c = command.Replace("\\", "\\\\").Replace("'", "\\'");
        return core.ExecuteScriptAsync($@"(function(){{if(window.notesEditor)window.notesEditor.apply('{c}');}})();");
    }

    public static Task InsertLinkViaEditorAsync(CoreWebView2 core, string url)
    {
        var safe = url.Replace("\\", "\\\\").Replace("'", "\\'");
        return core.ExecuteScriptAsync($@"(function(){{if(window.notesEditor)window.notesEditor.insertLink('{safe}');}})();");
    }

    public static Task ApplyCommandAsync(CoreWebView2 core, string command)
    {
        var c = command.Replace("\\", "\\\\").Replace("'", "\\'");
        var script = $@"(function(){{
var editor=document.getElementById('editor');
if(!editor)return;
editor.focus();
var selection=window.getSelection();
if(selection.rangeCount>0){{
var range=selection.getRangeAt(0);
var startContainer=range.startContainer,startOffset=range.startOffset;
var endContainer=range.endContainer,endOffset=range.endOffset;
document.execCommand('{c}',false,null);
try{{
var newRange=document.createRange();
newRange.setStart(startContainer,startOffset);
newRange.setEnd(endContainer,endOffset);
selection.removeAllRanges();
selection.addRange(newRange);
}}catch(e){{}}
}}else{{
document.execCommand('{c}',false,null);
}}
if(window.notesEditor&&window.notesEditor.getState){{ /* keep */ }}
}})();";
        return core.ExecuteScriptAsync(script);
    }

    public static Task ApplyCheckboxListAsync(CoreWebView2 core) =>
        ApplyEditorApiAsync(core, "checkboxList");


    public static Task InsertLinkAsync(CoreWebView2 core, string url)
    {
        var safe = url.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"");
        var script = $@"(function(){{
var editor=document.getElementById('editor');
if(!editor)return;
editor.focus();
var selection=window.getSelection();
if(selection.rangeCount>0&&!selection.isCollapsed){{
var range=selection.getRangeAt(0);
var startContainer=range.startContainer,startOffset=range.startOffset;
var endContainer=range.endContainer,endOffset=range.endOffset;
document.execCommand('createLink',false,'{safe}');
try{{
var newRange=document.createRange();
newRange.setStart(startContainer,startOffset);
newRange.setEnd(endContainer,endOffset);
selection.removeAllRanges();
selection.addRange(newRange);
}}catch(e){{}}
}}else{{
var link=document.createElement('a');
link.href='{safe}';
link.textContent='{safe}';
var range=selection.rangeCount>0?selection.getRangeAt(0):document.createRange();
range.deleteContents();
range.insertNode(link);
range.setStartAfter(link);
range.collapse(true);
selection.removeAllRanges();
selection.addRange(range);
}}
}})();";
        return core.ExecuteScriptAsync(script);
    }

    public static Task ApplyCodeBlockAsync(CoreWebView2 core)
    {
        const string script = """
(function() {
    const editor = document.getElementById('editor');
    if (!editor) return;
    editor.focus();
    const selection = window.getSelection();
    if (selection.rangeCount > 0 && !selection.isCollapsed) {
        const range = selection.getRangeAt(0);
        const selectedText = selection.toString();
        const code = document.createElement('code');
        code.textContent = selectedText;
        range.deleteContents();
        range.insertNode(code);
        const newRange = document.createRange();
        newRange.selectNodeContents(code);
        selection.removeAllRanges();
        selection.addRange(newRange);
    } else {
        document.execCommand('formatBlock', false, '<pre>');
    }
})();
""";
        return core.ExecuteScriptAsync(script);
    }

    public static Task InsertTableAsync(CoreWebView2 core)
    {
        const string script = """
(function() {
    const editor = document.getElementById('editor');
    if (!editor) return;
    editor.focus();
    const selection = window.getSelection();
    let range;
    if (selection.rangeCount > 0) {
        range = selection.getRangeAt(0);
    } else {
        range = document.createRange();
        range.selectNodeContents(editor);
        range.collapse(false);
    }
    const table = document.createElement('table');
    for (let i = 0; i < 3; i++) {
        const row = document.createElement('tr');
        for (let j = 0; j < 3; j++) {
            const cell = document.createElement(i === 0 ? 'th' : 'td');
            cell.innerHTML = '<br>';
            row.appendChild(cell);
        }
        table.appendChild(row);
    }
    range.deleteContents();
    range.insertNode(table);
    const firstCell = table.rows[0].cells[0];
    const newRange = document.createRange();
    newRange.setStart(firstCell, 0);
    newRange.setEnd(firstCell, 0);
    selection.removeAllRanges();
    selection.addRange(newRange);
})();
""";
        return core.ExecuteScriptAsync(script);
    }

    public static Task ApplyFormatBlockAsync(CoreWebView2 core, string blockTag)
    {
        var t = blockTag.Replace("\\", "\\\\").Replace("'", "\\'");
        var script = $@"(function(){{
var editor=document.getElementById('editor');
if(!editor)return;
editor.focus();
var selection=window.getSelection();
if(selection.rangeCount>0){{
var range=selection.getRangeAt(0);
var startContainer=range.startContainer,startOffset=range.startOffset;
var endContainer=range.endContainer,endOffset=range.endOffset;
document.execCommand('formatBlock',false,'<{t}>');
try{{
var newRange=document.createRange();
newRange.setStart(startContainer,startOffset);
newRange.setEnd(endContainer,endOffset);
selection.removeAllRanges();
selection.addRange(newRange);
}}catch(e){{}}
}}else{{
document.execCommand('formatBlock',false,'<{t}>');
}}
}})();";
        return core.ExecuteScriptAsync(script);
    }
}
