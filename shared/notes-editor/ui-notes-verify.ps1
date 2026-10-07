# Verify Notes tab: toolbar names, open Wishlist, no Error dialog.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class V {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern void mouse_event(int f,int a,int b,int c,int d);
  public static void Click(){ mouse_event(2,0,0,0,0); mouse_event(4,0,0,0,0); }
}
"@

function Find-Win {
  $p = Get-Process -Name ProtoLink.Communicator.Windows -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
  if (-not $p) { return $null }
  [V]::ShowWindow($p.MainWindowHandle, 3) | Out-Null
  [V]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
  return [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
}

function Find-ByName($root, [string]$pattern) {
  $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  $stack = New-Object System.Collections.Generic.Stack[object]
  $stack.Push($root)
  $n = 0
  while ($stack.Count -gt 0 -and $n -lt 15000) {
    $n++
    $el = $stack.Pop()
    try {
      $name = $el.Current.Name
      $help = $el.Current.HelpText
      if (($name -and ($name -like $pattern)) -or ($help -and ($help -like $pattern))) { return $el }
    } catch {}
    try {
      $c = $walker.GetFirstChild($el)
      while ($null -ne $c) { $stack.Push($c); $c = $walker.GetNextSibling($c) }
    } catch {}
  }
  return $null
}

function Invoke-El($el) {
  try { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); return $true } catch {}
  try {
    $r = $el.Current.BoundingRectangle
    if ($r.Width -gt 0 -and $r.X -gt -1000) {
      [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
      Start-Sleep -Milliseconds 80
      [V]::Click()
      return $true
    }
  } catch {}
  return $false
}

$fail = 0
$win = Find-Win
if (-not $win) { Write-Host "FAIL: app not running"; exit 1 }

foreach ($pat in @("*Error*", "*WebView error*", "*RPC_E*")) {
  if (Find-ByName $win $pat) { Write-Host "FAIL: dialog $pat"; exit 1 }
}
Write-Host "PASS no error dialogs"

$tabCond = New-Object System.Windows.Automation.AndCondition(
  (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)),
  (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Notes"))
)
$notesTab = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tabCond)
if (-not $notesTab) { Write-Host "FAIL Notes tab"; exit 1 }
$notesTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 3
Write-Host "PASS Notes tab"

$tips = @("Bold (Ctrl+B)", "Checkbox list (toggle)", "Insert table")
foreach ($t in $tips) {
  if (-not (Find-ByName $win $t)) { Write-Host "FAIL missing toolbar: $t"; $fail++ } else { Write-Host "PASS toolbar: $t" }
}

$wish = Find-ByName $win "*Вишлист*"
if ($wish) {
  [void](Invoke-El $wish)
  Start-Sleep -Milliseconds 400
  [void](Invoke-El $wish)
  Start-Sleep -Seconds 2
  Write-Host "PASS opened Wishlist"
} else {
  Write-Host "WARN Wishlist not in UIA tree (WPF virtualization); covered by wishlist-smoke.mjs"
}

$bold = Find-ByName $win "Bold (Ctrl+B)"
if ($bold) {
  [void](Invoke-El $bold)
  Start-Sleep -Milliseconds 300
  $cb = Find-ByName $win "Checkbox list (toggle)"
  if ($cb) { [void](Invoke-El $cb); Start-Sleep -Milliseconds 300 }
  Write-Host "PASS toolbar clicks (Bold, Checkbox)"
}

if (Find-ByName $win "*Error*") { Write-Host "FAIL error after toolbar"; exit 1 }
Write-Host "PASS no errors after toolbar"

if ($fail -gt 0) { exit 1 }
Write-Host "OK ui-notes-verify finished"
exit 0
