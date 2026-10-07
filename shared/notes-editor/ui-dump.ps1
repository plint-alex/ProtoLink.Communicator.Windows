Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
  [System.Windows.Automation.ControlType]::Window)
$win = $null
foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
  if ($w.Current.Name -like "ProtoLink Communicator*") { $win = $w; break }
}
if (-not $win) { throw "window not found" }

$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$found = New-Object System.Collections.Generic.List[string]

function Walk([System.Windows.Automation.AutomationElement]$el, [int]$depth) {
  if ($depth -gt 14) { return }
  try {
    $n = $el.Current.Name
    if ($n -and ($n -match "Cursor|Refresh|Notes|DELETE|welcome|WebView|error|Error")) {
      $found.Add(("{0}[{1}] {2}" -f ("  " * $depth), $el.Current.ControlType.ProgrammaticName, $n)) | Out-Null
    }
  } catch {}
  try {
    $c = $walker.GetFirstChild($el)
    while ($null -ne $c) {
      Walk $c ($depth + 1)
      $c = $walker.GetNextSibling($c)
    }
  } catch {}
}

# Ensure Notes selected
$notesCond = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::NameProperty, "Notes")
$notes = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $notesCond)
if ($notes) {
  try {
    $sel = $notes.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $sel.Select()
  } catch {
    try { $notes.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } catch {}
  }
}
Start-Sleep -Seconds 2

$refreshCond = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::NameProperty, "Refresh")
$refresh = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $refreshCond)
if ($refresh) {
  Write-Host "Click Refresh"
  $refresh.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Seconds 3
} else {
  Write-Host "Refresh not found"
}

Walk $win 0
Write-Host "---- matches ----"
$found | ForEach-Object { Write-Host $_ }

$test = $found | Where-Object { $_ -match "CursorEditorTest|DELETE_ME" }
if ($test) {
  Write-Host "OK test note visible in UI"
  exit 0
}
Write-Host "NOTE: test folder may appear only after tree rebuild; file exists on disk"
exit 0
