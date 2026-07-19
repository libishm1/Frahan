param(
    [int]$Seconds = 30,
    [string]$LogPath = "$PSScriptRoot\dialog_watchdog.log"
)
# Auto-dismisses modal dialogs (#32770) belonging to any Rhino process.
# Logs dialog title + static text + which button it clicked, then clicks
# OK/Yes/Close (first available), so headless canvas opens can't hang.
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
public static class Win32DW {
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumWindowsProc cb, IntPtr l);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    public const uint BM_CLICK = 0x00F5;
    public const uint WM_CLOSE = 0x0010;
    public static List<IntPtr> Tops = new List<IntPtr>();
    public static List<IntPtr> Kids = new List<IntPtr>();
    public static bool CollectTop(IntPtr h, IntPtr l) { Tops.Add(h); return true; }
    public static bool CollectKid(IntPtr h, IntPtr l) { Kids.Add(h); return true; }
}
'@
$rhinoPids = (Get-Process -Name Rhino* -ErrorAction SilentlyContinue).Id
if (-not $rhinoPids) { Write-Output "no rhino processes"; exit 0 }
$deadline = (Get-Date).AddSeconds($Seconds)
$dismissed = 0
while ((Get-Date) -lt $deadline) {
    [Win32DW]::Tops.Clear()
    [Win32DW]::EnumWindows([Win32DW+EnumWindowsProc]([Win32DW]::CollectTop), [IntPtr]::Zero) | Out-Null
    foreach ($h in [Win32DW]::Tops) {
        if (-not [Win32DW]::IsWindowVisible($h)) { continue }
        $cls = New-Object System.Text.StringBuilder 256
        [Win32DW]::GetClassName($h, $cls, 256) | Out-Null
        if ($cls.ToString() -ne '#32770') { continue }
        $pid2 = 0
        [Win32DW]::GetWindowThreadProcessId($h, [ref]$pid2) | Out-Null
        if ($rhinoPids -notcontains [int]$pid2) { continue }
        $title = New-Object System.Text.StringBuilder 512
        [Win32DW]::GetWindowText($h, $title, 512) | Out-Null
        [Win32DW]::Kids.Clear()
        [Win32DW]::EnumChildWindows($h, [Win32DW+EnumWindowsProc]([Win32DW]::CollectKid), [IntPtr]::Zero) | Out-Null
        $texts = @(); $buttons = @{}
        foreach ($k in [Win32DW]::Kids) {
            $kc = New-Object System.Text.StringBuilder 256
            [Win32DW]::GetClassName($k, $kc, 256) | Out-Null
            $kt = New-Object System.Text.StringBuilder 1024
            [Win32DW]::GetWindowText($k, $kt, 1024) | Out-Null
            $kcs = $kc.ToString(); $kts = $kt.ToString()
            if ($kcs -eq 'Static' -and $kts.Trim()) { $texts += $kts }
            if ($kcs -eq 'Button') {
                $clean = $kts -replace '&', ''
                if (-not $buttons.ContainsKey($clean)) { $buttons[$clean] = $k }
            }
        }
        $btn = [IntPtr]::Zero; $btnTxt = ''
        foreach ($pref in @('No', 'OK', 'Close', 'Continue', 'Ignore', 'Yes')) {
            if ($buttons.ContainsKey($pref)) { $btn = $buttons[$pref]; $btnTxt = $pref; break }
        }
        $stamp = Get-Date -Format 'HH:mm:ss'
        $entry = "[$stamp] pid=$pid2 title='$($title.ToString())' text='$($texts -join ' | ')' clicked='$btnTxt'"
        Add-Content -Path $LogPath -Value $entry
        Write-Output $entry
        if ($btn -ne [IntPtr]::Zero) { [Win32DW]::SendMessage($btn, [Win32DW]::BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
        else { [Win32DW]::PostMessage($h, [Win32DW]::WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
        $dismissed++
    }
    Start-Sleep -Milliseconds 500
}
Write-Output "watchdog done: dismissed=$dismissed"
