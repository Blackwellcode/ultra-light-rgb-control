# Removes Ultra Light RGB Control. Run from an elevated PowerShell.
# Configuration, theme and logs in %ProgramData%\UltraLightRgb stay unless -RemoveData is given.
# The LEDs keep their last colour until the next power cycle: nothing was ever saved to a device.
param([switch]$RemoveData)
$ErrorActionPreference = 'Stop'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw 'Run this script from an elevated PowerShell (Run as administrator).' }

$task = 'UltraLightRgb'
if (Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $task
    Unregister-ScheduledTask -TaskName $task -Confirm:$false
    "Task $task removed"
}
Get-Process UltraLightRgb, UltraLightRgb.Switch -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

Remove-Item (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Ultra Light RGB Control.lnk') -ErrorAction SilentlyContinue

$target = Join-Path $env:ProgramFiles 'UltraLightRgb'
# .new / .bak are leftovers of an interrupted update (see install.ps1)
foreach ($dir in $target, "$target.new", "$target.bak") {
    if (Test-Path -LiteralPath $dir) {
        Remove-Item -LiteralPath $dir -Recurse -Force
        "Removed $dir"
    }
}

$data = Join-Path $env:ProgramData 'UltraLightRgb'
if ($RemoveData -and (Test-Path $data)) {
    Remove-Item $data -Recurse -Force
    "Removed $data"
}
elseif (Test-Path $data) {
    "Kept $data (configuration, theme, logs); run with -RemoveData to delete it"
}
'UNINSTALL_OK'
