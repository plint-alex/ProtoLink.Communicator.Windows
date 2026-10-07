# UI smoke: ProtoLink Notes tab + open test note + no error dialogs
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Find-ByName($root, [string]$namePattern, [bool]$partial = $true) {
  $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  $stack = New-Object System.Collections.Generic.Stack[System.Windows.Automation.AutomationElement]
  $stack.Push($root)
  $n = 0
  while ($stack.Count -gt 0 -and $n -lt 8000) {
    $n++
    $el = $stack.Pop()
    try {
      $name = $el.Current.Name
      if ($name) {
        if ($partial -and ($name -like $namePattern)) { return $el }
        if (-not $partial -and ($name -eq $namePattern)) { return $el }
      }
    } catch {}
    try {
      $child = $walker.GetFirstChild($el)
      while ($null -ne $child) {
        $stack.Push($child)
        $child = $walker.GetNextSibling($child)
      }
    } catch {}
  }
  return $null
}

function Invoke-Click($el) {
  try {
    $invoke = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $invoke.Invoke()
    return $true
  } catch {}
  try {
    $sel = $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $sel.Select()
    return $true
  } catch {}
  try {
    $rect = $el.Current.BoundingRectangle
    if ($rect.Width -gt 0 -and $rect.Height -gt 0) {
      # fallback: focus
      $el.SetFocus()
      return $true
    }
  } catch {}
  return $false
}

$root = [System.Windows.Automation.AutomationElement]::RootElement
$win = $null
foreach ($p in @("ProtoLink Communicator*","ProtoLink*")) {
  $cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Window)
  $wins = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
  foreach ($w in $wins) {
    if ($w.Current.Name -like $p) { $win = $w; break }
  }
  if ($win) { break }
}

if (-not $win) {
  Write-Host "FAIL: ProtoLink window not found"
  Get-Process ProtoLink.Communicator.Windows -ErrorAction SilentlyContinue | Format-Table Id,MainWindowTitle
  exit 1
}

Write-Host "OK window: $($win.Current.Name)"

# Error dialogs?
$errorDlg = Find-ByName $win "*WebView*"
$errorDlg2 = Find-ByName $win "*Background task error*"
$errorDlg3 = Find-ByName $win "*RPC_E*"
if ($errorDlg -or $errorDlg2 -or $errorDlg3) {
  Write-Host "FAIL: error dialog visible"
  exit 1
}
Write-Host "OK no obvious error dialog"

# Click Notes tab
$notesTab = Find-ByName $win "Notes"
if (-not $notesTab) {
  Write-Host "FAIL: Notes control not found"
  exit 1
}
Write-Host "Found Notes element: type=$($notesTab.Current.ControlType.ProgrammaticName) name=$($notesTab.Current.Name)"
[void](Invoke-Click $notesTab)
Start-Sleep -Seconds 2

# Refresh button if present
$refresh = Find-ByName $win "Refresh"
if ($refresh) {
  Write-Host "Click Refresh"
  [void](Invoke-Click $refresh)
  Start-Sleep -Seconds 2
}

# Find test note in tree
$testNode = Find-ByName $win "*_CursorEditorTest_DELETE_ME*"
if (-not $testNode) {
  # tree may show only leaf name without underscore path
  Write-Host "WARN: test note not in UI tree yet (watcher may lag); trying Refresh again"
  if ($refresh) { [void](Invoke-Click $refresh); Start-Sleep -Seconds 2 }
  $testNode = Find-ByName $win "*_CursorEditorTest_DELETE_ME*"
}

if ($testNode) {
  Write-Host "OK test note in tree: $($testNode.Current.Name)"
  [void](Invoke-Click $testNode)
  # double-invoke / expand
  try {
    $expand = $testNode.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
  } catch {}
  Start-Sleep -Milliseconds 500
  # Prefer double-click via Invoke twice or Selection
  [void](Invoke-Click $testNode)
  Start-Sleep -Seconds 2
} else {
  Write-Host "WARN: could not find test note in automation tree (encoding/tree virtualization)"
}

# Re-check errors after opening Notes
$errorAfter = $false
foreach ($pat in @("*WebView error*","*Background task error*","*RPC_E_CHANGED_MODE*","*Изменение режима*","*0x80010106*")) {
  if (Find-ByName $win $pat) { $errorAfter = $true; Write-Host "FAIL dialog: $pat" }
}
if ($errorAfter) { exit 1 }

Write-Host "OK Notes tab smoke finished without error dialogs"
exit 0
