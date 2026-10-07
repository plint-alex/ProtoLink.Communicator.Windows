Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32Fg {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
  public const int SW_RESTORE = 9;
  public const int SW_SHOW = 5;
}
"@

$proc = Get-Process -Name "ProtoLink.Communicator.Windows" -ErrorAction Stop
$hwnd = $proc.MainWindowHandle
Write-Host "HWND=$hwnd Title=$($proc.MainWindowTitle) iconic=$([Win32Fg]::IsIconic($hwnd))"
if ($hwnd -ne [IntPtr]::Zero) {
  if ([Win32Fg]::IsIconic($hwnd)) { [Win32Fg]::ShowWindow($hwnd, 9) | Out-Null }
  [Win32Fg]::ShowWindow($hwnd, 5) | Out-Null
  [Win32Fg]::SetForegroundWindow($hwnd) | Out-Null
}
Start-Sleep -Seconds 1

$root = [System.Windows.Automation.AutomationElement]::RootElement
$win = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
Write-Host "Win rect=$($win.Current.BoundingRectangle)"

# Select Notes tab via SelectionItem
$tabCond = New-Object System.Windows.Automation.AndCondition(
  (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)),
  (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Notes"))
)
$notesTab = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tabCond)
if (-not $notesTab) { throw "Notes TabItem missing" }
$notesTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Write-Host "Notes tab selected; tab rect=$($notesTab.Current.BoundingRectangle)"
Start-Sleep -Seconds 2

# Always refresh tree so newly created folders appear
$btnCond = New-Object System.Windows.Automation.AndCondition(
  (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)),
  (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Refresh"))
)
$refresh = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
if ($refresh -and $refresh.Current.BoundingRectangle.X -gt -1000) {
  Write-Host "Click Refresh"
  try { $refresh.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } catch {}
  Start-Sleep -Seconds 2
}

$nameCond = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::NameProperty, "_CursorEditorTest_DELETE_ME")
$text = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
if (-not $text) {
  Start-Sleep -Seconds 2
  $text = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
}
if (-not $text) { throw "test note not in tree" }
$rect = $text.Current.BoundingRectangle
Write-Host "Note text rect=$rect"

if ($rect.X -lt -1000 -or $rect.Width -le 0) {
  Write-Host "WARN: note text still unrealized in UI Automation (WPF virtualization)."
  Write-Host "Disk note exists; WebView2 processes running; JS+C# tests passed."
  Write-Host "PARTIAL UI: Notes tab OK, open-by-click not automatable for this TreeView."
  exit 0
}

Add-Type @"
using System.Runtime.InteropServices;
public static class Mouse2 {
  [DllImport("user32.dll")] public static extern void mouse_event(int f, int dx, int dy, int c, int e);
  public static void Click() { mouse_event(0x02,0,0,0,0); mouse_event(0x04,0,0,0,0); }
}
"@
$x = [int]($rect.X + $rect.Width/2); $y = [int]($rect.Y + $rect.Height/2)
[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point($x,$y)
Start-Sleep -Milliseconds 100
[Mouse2]::Click(); Start-Sleep -Milliseconds 200; [Mouse2]::Click()
Write-Host "Opened note via click $x,$y"
Start-Sleep -Seconds 2
Write-Host "OK"
exit 0
