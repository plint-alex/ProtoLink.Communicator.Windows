Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
  [System.Windows.Automation.ControlType]::Window)
$win = $null
foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
  if ($w.Current.Name -like "ProtoLink Communicator*") { $win = $w; break }
}
if (-not $win) { throw "no win" }

$tabCond = New-Object System.Windows.Automation.AndCondition(
  (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)),
  (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Notes"))
)
$notes = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tabCond)
Write-Host "Notes tab found:" ($null -ne $notes) "selected?" 
try {
  $pat = $notes.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
  Write-Host "IsSelected=" $pat.Current.IsSelected
  $pat.Select()
  Start-Sleep -Seconds 1
  Write-Host "IsSelected after=" $pat.Current.IsSelected
} catch { Write-Host $_ }

$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
function Walk($el, $depth) {
  if ($depth -gt 8) { return }
  try {
    $n = $el.Current.Name
    $t = $el.Current.ControlType.ProgrammaticName
    $r = $el.Current.BoundingRectangle
    $line = ("{0}{1} name=[{2}] {3}x{4}" -f ("  " * $depth), $t, $n, [int]$r.Width, [int]$r.Height)
    if ($r.Width -gt 0 -or $depth -lt 4) { Write-Host $line }
  } catch {}
  try {
    $c = $walker.GetFirstChild($el)
    $count = 0
    while ($null -ne $c -and $count -lt 40) {
      Walk $c ($depth + 1)
      $c = $walker.GetNextSibling($c)
      $count++
    }
  } catch {}
}
Write-Host "=== TREE ==="
Walk $win 0
