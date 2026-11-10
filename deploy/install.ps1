# Installs or updates Ultra Light RGB Control: copies the app to Program Files, locks down its data
# folder and registers a SYSTEM task that starts it at boot. Run from an elevated PowerShell.
#   -Source      folder with UltraLightRgb.exe (default: .\app next to this script, as in the release zip)
#   -ResultFile  optional transcript, for unattended runs
#   -Bench       measure every theme on the LCD before the task starts again
param(
    [string]$Source = (Join-Path $PSScriptRoot 'app'),
    [string]$ResultFile,
    [switch]$Bench
)
$ErrorActionPreference = 'Stop'
if ($ResultFile) { Start-Transcript -Path $ResultFile -Force | Out-Null }
try {
    $admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $admin) { throw 'Run this script from an elevated PowerShell (Run as administrator).' }
    if (-not (Test-Path (Join-Path $Source 'UltraLightRgb.exe'))) { throw "UltraLightRgb.exe not found in $Source" }

    # framework-dependent build: the theme switcher needs the Windows Desktop runtime 8
    $desktop = Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.WindowsDesktop.App'
    if (-not (Get-ChildItem $desktop -Directory -Filter '8.*' -ErrorAction SilentlyContinue)) {
        throw 'The .NET 8 Desktop Runtime (x64) is missing: https://dotnet.microsoft.com/download/dotnet/8.0'
    }

    $task = 'UltraLightRgb'
    $target = Join-Path $env:ProgramFiles 'UltraLightRgb'
    $data = Join-Path $env:ProgramData 'UltraLightRgb'
    $startMenu = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'

    # an open switcher window locks its DLL and would abort the copy with the task already stopped
    if (Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue) { Stop-ScheduledTask -TaskName $task }
    Get-Process UltraLightRgb, UltraLightRgb.Switch -ErrorAction SilentlyContinue | Stop-Process -Force

    # predecessor of this project (TrLcd): stop it and take over its theme, leave its files in place
    if (Get-ScheduledTask -TaskName 'TrLcd' -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName 'TrLcd'
        Unregister-ScheduledTask -TaskName 'TrLcd' -Confirm:$false
        'Removed the old TrLcd task (its folder stays, delete it yourself when no longer needed)'
    }
    Get-Process TrLcd, TrLcdSwitch -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-Item (Join-Path $startMenu 'TrLcd Themes.lnk') -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1

    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Copy-Item -Path (Join-Path $Source '*') -Destination $target -Recurse -Force

    # configuration is read by SYSTEM: only admins may change it. The switcher, running as a normal
    # user, writes one word into state\theme.txt, which the service validates against a fixed list.
    # SIDs instead of names, because localized Windows renames the groups.
    New-Item -ItemType Directory -Path (Join-Path $data 'state'), (Join-Path $data 'logs') -Force | Out-Null
    icacls $data /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "icacls $data failed with exit code $LASTEXITCODE" }
    icacls (Join-Path $data 'state') /grant '*S-1-5-32-545:(OI)(CI)M' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "icacls state failed with exit code $LASTEXITCODE" }

    $oldTheme = Join-Path $env:ProgramData 'TrLcd\theme.txt'
    $newTheme = Join-Path $data 'state\theme.txt'
    if ((Test-Path $oldTheme) -and -not (Test-Path $newTheme)) {
        Copy-Item $oldTheme $newTheme
        "Theme taken over from TrLcd: $(Get-Content $newTheme -TotalCount 1)"
    }

    if ($Bench) {
        & (Join-Path $target 'UltraLightRgb.exe') --bench
        "bench_exit=$LASTEXITCODE"
    }

    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $startMenu 'Ultra Light RGB Control.lnk'))
    $shortcut.TargetPath = Join-Path $target 'UltraLightRgb.Switch.exe'
    $shortcut.WorkingDirectory = $target
    $shortcut.Description = 'Pick the LCD and LED theme'
    $shortcut.Save()

    $action = New-ScheduledTaskAction -Execute (Join-Path $target 'UltraLightRgb.exe') -Argument '--service' -WorkingDirectory $target
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $trigger.Delay = 'PT30S'
    # SYSTEM with highest privileges: the CPU temperature sensor needs it
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 `
        -RestartInterval (New-TimeSpan -Minutes 1) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $task -Action $action -Trigger $trigger -Principal $principal `
        -Settings $settings -Description 'Ultra Light RGB Control: CPU/GPU temperature on LCD and RGB' -Force | Out-Null
    Start-ScheduledTask -TaskName $task
    Start-Sleep -Seconds 4

    $proc = Get-Process UltraLightRgb -ErrorAction SilentlyContinue
    if (-not $proc) { throw "The task started but UltraLightRgb is not running, see $data\logs\service.log" }
    "task=$((Get-ScheduledTask -TaskName $task).State) pid=$($proc.Id)"
    "UltraLightRgb.dll SHA256=$((Get-FileHash (Join-Path $target 'UltraLightRgb.dll')).Hash)"
    "Configuration: $data\devices.json (optional, example: $target\devices.example.json)"
    'INSTALL_OK'
}
catch {
    "INSTALL_FAILED: $($_.Exception.Message)"
    if (-not $ResultFile) { exit 1 }
}
finally {
    if ($ResultFile) { Stop-Transcript | Out-Null }
}
