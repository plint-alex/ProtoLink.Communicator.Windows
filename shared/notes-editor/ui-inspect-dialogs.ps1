Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
  [System.Windows.Automation.ControlType]::Window)

Write-Host "=== TOP WINDOWS ==="
foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
  $r = $w.Current.BoundingRectangle
  Write-Host ("WIN [{0}] class={1} x={2:0} w={3:0} h={4:0}" -f $w.Current.Name, $w.Current.ClassName, $r.X, $r.Width, $r.Height)
}

# Close any dialog with Close button under ProtoLink
foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
  if ($w.Current.Name -notlike "ProtoLink*") { continue }
  $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  $stack = New-Object System.Collections.Generic.Stack[System.Windows.Automation.AutomationElement]
  $stack.Push($w)
  $n = 0
  while ($stack.Count -gt 0 -and $n -lt 5000) {
    $n++
    $el = $stack.Pop()
    try {
      $name = $el.Current.Name
      $type = $el.Current.ControlType.ProgrammaticName
      $rr = $el.Current.BoundingRectangle
      if ($name -and $rr.Width -gt 0 -and $rr.X -gt -1000) {
        if ($type -match 'Window|Dialog|Button|Text|Tree|Tab|Document|Edit|Combo') {
          Write-Host ("{0} [{1}]" -f $type, $name)
        }
      }
      if ($name -eq "Close" -and $type -match "Button") {
        Write-Host "Closing dialog button..."
        try { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } catch {}
      }
    } catch {}
    try {
      $c = $walker.GetFirstChild($el)
      while ($null -ne $c) { $stack.Push($c); $c = $walker.GetNextSibling($c) }
    } catch {}
  }
}

Start-Sleep -Seconds 1
Write-Host "=== AFTER CLOSE ==="
$proc = Get-Process ProtoLink.Communicator.Windows -ErrorAction SilentlyContinue | Select-Object -First 1
Write-Host ("Process title={0} hwnd={1}" -f $proc.MainWindowTitle, $proc.MainWindowHandle)
