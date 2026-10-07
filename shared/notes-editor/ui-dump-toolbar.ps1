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

$tabCond = New-Object System.Windows.Automation.AndCondition(
  (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)),
  (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Notes"))
)
$notes = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tabCond)
if ($notes) {
  $notes.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
Start-Sleep -Seconds 2

$btnCond = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
  [System.Windows.Automation.ControlType]::Button)
$btns = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
Write-Host "BUTTONS:" $btns.Count
foreach ($b in $btns) {
  $n = $b.Current.Name
  $h = $b.Current.HelpText
  $r = $b.Current.BoundingRectangle
  if ($r.Width -gt 0 -and $r.X -gt -1000) {
    Write-Host ("BTN name=[{0}] help=[{1}] x={2:0} w={3:0}" -f $n, $h, $r.X, $r.Width)
  }
}

$treeCond = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
  [System.Windows.Automation.ControlType]::TreeItem)
$items = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $treeCond)
Write-Host "TREEITEMS:" $items.Count
foreach ($i in $items) {
  Write-Host ("TREE [{0}]" -f $i.Current.Name)
}

$textCond = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
  [System.Windows.Automation.ControlType]::Text)
$texts = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)
Write-Host "TEXT nodes (sample):"
$shown = 0
foreach ($t in $texts) {
  $n = $t.Current.Name
  if (-not $n) { continue }
  if ($n.Length -gt 80) { $n = $n.Substring(0, 80) }
  Write-Host ("TEXT [{0}]" -f $n)
  $shown++
  if ($shown -ge 40) { break }
}
