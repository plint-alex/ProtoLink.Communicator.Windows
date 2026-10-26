using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Capture/restore Notes editor scroll + caret across same-page content refresh.
/// Prefer in-place #editor update (keeps document listeners) over NavigateToString remount.
/// </summary>
public static class NotesEditorViewport
{
    private static readonly JsonSerializerOptions JsStringOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public sealed record State(double ScrollY, int? CaretStart, int? CaretEnd);

    public static string BuildCaptureScript() => """
(function(){
var editor=document.getElementById('editor');
var scrollY=window.scrollY||document.documentElement.scrollTop||document.body.scrollTop||0;
var caretStart=null,caretEnd=null;
if(editor){
var sel=window.getSelection();
if(sel&&sel.rangeCount>0){
var range=sel.getRangeAt(0);
if(editor.contains(range.commonAncestorContainer)){
function offsetInEditor(node,offset){
var pre=document.createRange();
pre.selectNodeContents(editor);
pre.setEnd(node,offset);
return pre.toString().length;
}
try{
caretStart=offsetInEditor(range.startContainer,range.startOffset);
caretEnd=offsetInEditor(range.endContainer,range.endOffset);
}catch(e){}
}
}
}
return JSON.stringify({scrollY:scrollY,caretStart:caretStart,caretEnd:caretEnd});
})();
""";

    public static string BuildApplyInnerHtmlScript(string innerHtml, State? viewport)
    {
        var htmlJson = JsonSerializer.Serialize(innerHtml ?? string.Empty, JsStringOptions);
        var scroll = (viewport?.ScrollY ?? 0).ToString(CultureInfo.InvariantCulture);
        var start = viewport?.CaretStart is int s ? s.ToString(CultureInfo.InvariantCulture) : "null";
        var end = viewport?.CaretEnd is int e ? e.ToString(CultureInfo.InvariantCulture) : "null";
        return $$"""
(function(){
var editor=document.getElementById('editor');
if(!editor)return false;
editor.innerHTML={{htmlJson}};
editor.querySelectorAll('ul.checkbox-list li label[for]').forEach(function(label){
label.removeAttribute('for');
});
var scrollY={{scroll}};
var caretStart={{start}};
var caretEnd={{end}};
window.scrollTo(0,scrollY);
if(document.documentElement)document.documentElement.scrollTop=scrollY;
if(document.body)document.body.scrollTop=scrollY;
if(caretStart!=null){
var targetEnd=caretEnd!=null?caretEnd:caretStart;
var walker=document.createTreeWalker(editor,NodeFilter.SHOW_TEXT,null);
var pos=0,startNode=null,startOff=0,endNode=null,endOff=0;
while(walker.nextNode()){
var n=walker.currentNode;
var len=(n.textContent||'').length;
if(startNode==null&&pos+len>=caretStart){startNode=n;startOff=caretStart-pos;}
if(endNode==null&&pos+len>=targetEnd){endNode=n;endOff=targetEnd-pos;break;}
pos+=len;
}
if(startNode){
try{
var r=document.createRange();
var sn=startNode,so=Math.min(startOff,(sn.textContent||'').length);
var en=endNode||startNode,eo=Math.min(endOff,((endNode||startNode).textContent||'').length);
r.setStart(sn,so);
r.setEnd(en,eo);
var sel=window.getSelection();
sel.removeAllRanges();
sel.addRange(r);
editor.focus();
}catch(err){}
}
}
return true;
})();
""";
    }

    public static string BuildRestoreScript(State viewport)
    {
        var scroll = viewport.ScrollY.ToString(CultureInfo.InvariantCulture);
        var start = viewport.CaretStart is int s ? s.ToString(CultureInfo.InvariantCulture) : "null";
        var end = viewport.CaretEnd is int e ? e.ToString(CultureInfo.InvariantCulture) : "null";
        return $$"""
(function(){
var editor=document.getElementById('editor');
var scrollY={{scroll}};
var caretStart={{start}};
var caretEnd={{end}};
window.scrollTo(0,scrollY);
if(document.documentElement)document.documentElement.scrollTop=scrollY;
if(document.body)document.body.scrollTop=scrollY;
if(!editor||caretStart==null)return;
var targetEnd=caretEnd!=null?caretEnd:caretStart;
var walker=document.createTreeWalker(editor,NodeFilter.SHOW_TEXT,null);
var pos=0,startNode=null,startOff=0,endNode=null,endOff=0;
while(walker.nextNode()){
var n=walker.currentNode;
var len=(n.textContent||'').length;
if(startNode==null&&pos+len>=caretStart){startNode=n;startOff=caretStart-pos;}
if(endNode==null&&pos+len>=targetEnd){endNode=n;endOff=targetEnd-pos;break;}
pos+=len;
}
if(!startNode)return;
try{
var r=document.createRange();
r.setStart(startNode,Math.min(startOff,(startNode.textContent||'').length));
r.setEnd(endNode||startNode,Math.min(endOff,((endNode||startNode).textContent||'').length));
var sel=window.getSelection();
sel.removeAllRanges();
sel.addRange(r);
editor.focus();
}catch(err){}
})();
""";
    }

    /// <summary>Parse CoreWebView2 ExecuteScriptAsync JSON-encoded return value.</summary>
    public static State? ParseCaptureResult(string? executeScriptResult)
    {
        if (string.IsNullOrWhiteSpace(executeScriptResult) || executeScriptResult == "null")
            return null;

        string payload = executeScriptResult;
        try
        {
            // ExecuteScriptAsync wraps string results as JSON strings.
            if (payload.Length >= 2 && payload[0] == '"')
                payload = JsonSerializer.Deserialize<string>(payload) ?? payload;
        }
        catch (JsonException)
        {
            // Use raw payload.
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var scrollY = root.TryGetProperty("scrollY", out var sy) ? sy.GetDouble() : 0;
            int? caretStart = null;
            int? caretEnd = null;
            if (root.TryGetProperty("caretStart", out var cs) && cs.ValueKind == JsonValueKind.Number)
                caretStart = cs.GetInt32();
            if (root.TryGetProperty("caretEnd", out var ce) && ce.ValueKind == JsonValueKind.Number)
                caretEnd = ce.GetInt32();
            return new State(scrollY, caretStart, caretEnd);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static async Task<State?> CaptureAsync(CoreWebView2 core)
    {
        ArgumentNullException.ThrowIfNull(core);
        try
        {
            var raw = await core.ExecuteScriptAsync(BuildCaptureScript());
            return ParseCaptureResult(raw);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Replace #editor HTML in the live document and restore viewport. Returns false if no editor (caller should NavigateToString).
    /// </summary>
    public static async Task<bool> TryApplyInnerHtmlAsync(CoreWebView2 core, string innerHtml, State? viewport)
    {
        ArgumentNullException.ThrowIfNull(core);
        try
        {
            var raw = await core.ExecuteScriptAsync(BuildApplyInnerHtmlScript(innerHtml, viewport));
            return raw is "true" or "\"true\"";
        }
        catch
        {
            return false;
        }
    }

    public static async Task RestoreAsync(CoreWebView2 core, State viewport)
    {
        ArgumentNullException.ThrowIfNull(core);
        try
        {
            await core.ExecuteScriptAsync(BuildRestoreScript(viewport));
        }
        catch
        {
            // Best-effort after remount.
        }
    }
}
