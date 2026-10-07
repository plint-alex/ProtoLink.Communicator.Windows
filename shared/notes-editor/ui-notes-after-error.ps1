Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Fg2 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@

$proc = Get-Process ProtoLink.Communicator.Windows | Select-Object -First 1
[Fg2]::ShowWindow($proc.MainWindowHandle, 5) | Out-Null
[Fg2]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
  [System.Windows.Automation.ControlType]::Window)
$win = $null
foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
  if ($w.Current.Name -like "ProtoLink Communicator*") { $win = $w; break }
}
if (-not $win) { throw "no win" }

# Dump any Error windows and text
foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
  if ($w.Current.Name -eq "Error" -or $w.Current.Name -like "*Error*") {
    Write-Host "=== ERROR WINDOW ==="
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $stack = New-Object System.Collections.Generic.Stack[object]
    $stack.Push($w)
    while ($stack.Count -gt 0) {
      $el = $stack.Pop()
      try {
        $n = $el.Current.Name
        if ($n) { Write-Host ("ERR [{0}] {1}" -f $el.Current.ControlType.ProgrammaticName, $n) }
      } catch {}
      try {
        $c = $walker.GetFirstChild($el)
        while ($null -ne $c) { $stack.Push($c); $c = $walker.GetNextSibling($c) }
      } catch {}
    }
  }
}

# Select Notes
$tabCond = New-Object System.Windows.Automation.AndCondition(
  (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)),
  (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Notes"))
)
$notes = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tabCond)
$notes.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 2

$refreshCond = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::NameProperty, "Refresh")
$refresh = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $refreshCond)
if ($refresh) {
  Write-Host "Click Refresh"
  try { $refresh.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } catch {}
  Start-Sleep -Seconds 2
}

$btnCond = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
  [System.Windows.Automation.ControlType]::Button)
Write-Host "=== BUTTONS AFTER NOTES ==="
foreach ($b in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)) {
  $r = $b.Current.BoundingRectangle
  if ($r.Width -le 0 -or $r.X -lt -1000) { continue }
  Write-Host ("BTN name=[{0}] help=[{1}]" -f $b.Current.Name, $b.Current.HelpText)
}

$treeCond = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
  [System.Windows.Automation.ControlType]::TreeItem)
$items = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $treeCond)
Write-Host "TREEITEMS:" $items.Count
foreach ($i in $items) { Write-Host ("TREE [{0}]" -f $i.Current.Name) }

# Also search Text for wishlist substring
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$stack = New-Object System.Collections.Generic.Stack[object]
$stack.Push($win)
$n = 0
Write-Host "=== NAME HITS ==="
while ($stack.Count -gt 0 -and $n -lt 8000) {
  $n++
  $el = $stack.Pop()
  try {
    $name = $el.Current.Name
    if ($name -and ($name -match "wish|Wish|Refresh|Bold|Checkbox|welcome|Normal|Heading|B$|•")) {
      Write-Host ("HIT [{0}] {1}" -f $el.Current.ControlType.ProgrammaticName, $name)
    }
  } catch {}
  try {
    $c = $walker.GetFirstChild($el)
    while ($null -ne $c) { $stack.Push($c); $c = $walker.GetNextSibling($c) }
  } catch {}
}
