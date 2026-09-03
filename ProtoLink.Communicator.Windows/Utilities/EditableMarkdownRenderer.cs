using System.Text;
using System.Text.RegularExpressions;

namespace ProtoLink.Communicator.Windows.Utilities;

public class EditableMarkdownRenderer
{
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
ul.checkbox-list li input[type="checkbox"]{margin-right:8px;margin-top:2px;cursor:pointer;flex-shrink:0;}
ul.checkbox-list li label{cursor:pointer;flex:1;margin:0;}
ul.checkbox-list li:has(input:checked){text-decoration:line-through;opacity:0.6;}
table{border-collapse:collapse;margin:0.5em 0;}
td,th{border:1px solid #dfe1e6;padding:6px;}
""";
        var doc = $$"""
<!DOCTYPE html>
<html>
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1.0">
<style>
html,body{height:100%;margin:0;}
body{font-family:'Segoe UI',sans-serif;line-height:1.6;padding:20px;min-height:100%;box-sizing:border-box;}
#editor{outline:none;min-height:100%;font-size:14px;padding:20px;box-sizing:border-box;}
#editor:focus{outline:2px solid #0066cc;}
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
(function(){
var editor=document.getElementById('editor');
var base64=editor.getAttribute('data-content-base64');
if(base64){
try{
var bin=atob(base64);
var bytes=new Uint8Array(bin.length);
for(var i=0;i<bin.length;i++)bytes[i]=bin.charCodeAt(i);
var html=new TextDecoder('utf-8').decode(bytes);
var d=document.createElement('div');d.innerHTML=html;
while(d.firstChild)editor.appendChild(d.firstChild);
editor.removeAttribute('data-content-base64');
}catch(e){editor.innerHTML='<p><br></p>';editor.removeAttribute('data-content-base64');}
}
})();
document.addEventListener('keydown',function(e){if(e.key==='Control')document.body.dataset.ctrlKey='true';});
document.addEventListener('keyup',function(e){if(e.key==='Control')document.body.dataset.ctrlKey='false';});
document.addEventListener('click',function(e){
var a=e.target&&e.target.closest?e.target.closest('a'):null;
if(e.ctrlKey&&a&&a.href&&(a.href.startsWith('http://')||a.href.startsWith('https://'))){
e.preventDefault();e.stopPropagation();
if(window.chrome&&window.chrome.webview)window.chrome.webview.postMessage(JSON.stringify({type:'openLink',url:a.href}));
}
},true);
editor.addEventListener('input',function(){clearTimeout(window._noteT);window._noteT=setTimeout(function(){
if(window.chrome&&window.chrome.webview)window.chrome.webview.postMessage(JSON.stringify({type:'contentChanged'}));
},300);});
editor.addEventListener('copy',function(e){
var sel=window.getSelection();
if(!sel||!sel.rangeCount||sel.isCollapsed)return;
var range=sel.getRangeAt(0);
if(!editor.contains(range.commonAncestorContainer))return;
try{
var htmlDiv=document.createElement('div');
htmlDiv.appendChild(range.cloneContents());
var htmlClip=htmlDiv.innerHTML;
var host=document.createElement('div');
host.setAttribute('aria-hidden','true');
host.style.cssText='position:fixed;left:-10000px;top:0;width:10000px;min-height:1px;opacity:0;pointer-events:none;';
host.appendChild(range.cloneContents());
document.body.appendChild(host);
var plain=(host.innerText||'').replace(/\r\n?/g,'\n');
var prevPlain;
do{prevPlain=plain;plain=plain.replace(/(\S)(?:\n\s*){2,}(\S)/g,'$1\n$2');}while(plain!==prevPlain);
plain=plain.replace(/^\n+/,'').replace(/\n+$/,'');
document.body.removeChild(host);
e.clipboardData.setData('text/plain',plain);
if(htmlClip)e.clipboardData.setData('text/html',htmlClip);
e.preventDefault();
}catch(err){}
});
window.getHtml=function(){return editor.innerHTML||'';};
</script>
</body>
</html>
""";
        return doc.Replace("/*__NOTE_EXTRA_STYLES__*/", extraStyles.Trim(), StringComparison.Ordinal);
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
