# Full Notes UI smoke: Notes tab, Wishlist, toolbar clicks, typing, no error dialogs.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class UiSmokeNative {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern void mouse_event(int f, int dx, int dy, int c, int e);
  public static void Click() { mouse_event(0x02,0,0,0,0); mouse_event(0x04,0,0,0,0); }
}
"@

function Find-Win {
  $procs = Get-Process -Name "ProtoLink.Communicator.Windows" -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero }
  if (-not $procs) { return $null }
  $p = $procs | Select-Object -First 1
  if ([UiSmokeNative]::IsIconic($p.MainWindowHandle)) { [UiSmokeNative]::ShowWindow($p.MainWindowHandle, 9) | Out-Null }
  [UiSmokeNative]::ShowWindow($p.MainWindowHandle, 5) | Out-Null
  [UiSmokeNative]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
  return [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
}

function Find-ByName($root, [string]$pattern) {
  $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  $stack = New-Object System.Collections.Generic.Stack[System.Windows.Automation.AutomationElement]
  $stack.Push($root)
  $n = 0
  while ($stack.Count -gt 0 -and $n -lt 12000) {
    $n++
    $el = $stack.Pop()
    try {
      $name = $el.Current.Name
      $help = $el.Current.HelpText
      if (($name -and ($name -like $pattern)) -or ($help -and ($help -like $pattern))) { return $el }
    } catch {}
    try {
      $child = $walker.GetFirstChild($el)
      while ($null -ne $child) { $stack.Push($child); $child = $walker.GetNextSibling($child) }
    } catch {}
  }
  return $null
}

function Invoke-El($el) {
  if (-not $el) { return $false }
  try {
    $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    return $true
  } catch {}
  try {
    $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    return $true
  } catch {}
  try {
    $r = $el.Current.BoundingRectangle
    if ($r.Width -gt 0 -and $r.X -gt -1000) {
      $x = [int]($r.X + $r.Width / 2); $y = [int]($r.Y + $r.Height / 2)
      [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point($x, $y)
      Start-Sleep -Milliseconds 80
      [UiSmokeNative]::Click()
      return $true
    }
  } catch {}
  return $false
}

function Assert-NoErrors($win) {
  foreach ($pat in @("*WebView error*", "*Background task error*", "*RPC_E*", "*0x80010106*")) {
    $dlg = Find-ByName $win $pat
    if ($dlg) { throw "Error dialog visible: $pat / $($dlg.Current.Name)" }
  }
}

$script:fail = 0
function Ok($msg) { Write-Host "PASS $msg" }
function Fail($msg) { Write-Host "FAIL $msg"; $script:fail++ }

$win = Find-Win
if (-not $win) { Write-Host "FAIL: ProtoLink window not found"; exit 1 }
Ok "window $($win.Current.Name)"
Assert-NoErrors $win
Ok "no error dialogs at start"

$tabCond = New-Object System.Windows.Automation.AndCondition(
  (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)),
  (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Notes"))
)
$notesTab = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tabCond)
if (-not $notesTab) { Fail "Notes tab missing" } else {
  $notesTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Ok "Notes tab selected"
}
Start-Sleep -Seconds 2

$refresh = Find-ByName $win "Refresh"
if ($refresh) { [void](Invoke-El $refresh); Start-Sleep -Seconds 2; Ok "Refresh" }

$wish = Find-ByName $win "*Вишлист*"
if (-not $wish) { $wish = Find-ByName $win "*Wishlist*" }
if ($wish) {
  Ok "Wishlist in tree: $($wish.Current.Name)"
  [void](Invoke-El $wish)
  Start-Sleep -Milliseconds 400
  [void](Invoke-El $wish)
  Start-Sleep -Seconds 2
} else {
  Fail "Wishlist not found in automation tree"
}

Assert-NoErrors $win
Ok "no errors after open Wishlist"

$toolbarTips = @(
  "Bold (Ctrl+B)",
  "Italic (Ctrl+I)",
  "Underline (Ctrl+U)",
  "Strikethrough",
  "Bullet list",
  "Numbered list",
  "Checkbox list (toggle)",
  "Decrease indent (Shift+Tab)",
  "Increase indent (Tab)",
  "Insert link (Ctrl+K)",
  "Code block",
  "Insert table"
)

$clicked = 0
foreach ($tip in $toolbarTips) {
  $btn = Find-ByName $win $tip
  if (-not $btn) {
    Write-Host "WARN toolbar not found by name: $tip"
    continue
  }
  if ($tip -like "Insert link*") {
    Ok "toolbar present: $tip (skip invoke - dialog)"
    $clicked++
    continue
  }
  if (Invoke-El $btn) {
    Start-Sleep -Milliseconds 250
    Assert-NoErrors $win
    Ok "toolbar click: $tip"
    $clicked++
  } else {
    Fail "could not invoke: $tip"
  }
}

$combo = Find-ByName $win "*Heading*"
if (-not $combo) { $combo = Find-ByName $win "*Normal*" }
if ($combo) { Ok "heading combo present: $($combo.Current.Name)" }

$wr = $win.Current.BoundingRectangle
$ex = [int]($wr.X + $wr.Width * 0.72)
$ey = [int]($wr.Y + $wr.Height * 0.55)
[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point($ex, $ey)
Start-Sleep -Milliseconds 100
[UiSmokeNative]::Click()
Start-Sleep -Milliseconds 300
$hwnd = (Get-Process ProtoLink.Communicator.Windows | Select-Object -First 1).MainWindowHandle
[UiSmokeNative]::SetForegroundWindow($hwnd) | Out-Null

[System.Windows.Forms.SendKeys]::SendWait("UI_SMOKE_MARKER")
Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait("^z")
Start-Sleep -Milliseconds 400
Ok "typing + Ctrl+Z attempted in editor region"

$cbBtn = Find-ByName $win "Checkbox list (toggle)"
if ($cbBtn) {
  [void](Invoke-El $cbBtn)
  Start-Sleep -Milliseconds 200
  [System.Windows.Forms.SendKeys]::SendWait("x")
  Start-Sleep -Milliseconds 200
  [System.Windows.Forms.SendKeys]::SendWait("^z")
  Start-Sleep -Milliseconds 200
  [System.Windows.Forms.SendKeys]::SendWait("^z")
  Ok "checkbox toggle + type + undo"
}

Assert-NoErrors $win
Ok "no errors at end"

Write-Host ""
Write-Host "Toolbar tips clicked/seen: $clicked / $($toolbarTips.Count)"
if ($script:fail -gt 0) { Write-Host "FAILED $($script:fail) checks"; exit 1 }
Write-Host "OK full Notes UI smoke finished"
exit 0
