# Installs or updates Ultra Light RGB Control: copies the app to Program Files, locks down its data
# folder and registers a SYSTEM task that starts it at boot. Run from an elevated PowerShell.
#   -Source        folder with UltraLightRgb.exe (default: .\app next to this script, as in the release zip)
#   -ResultFile    optional transcript, for unattended runs (the exit code is 1 on failure either way)
#   -Bench         measure every theme on the LCD before the task starts again
#   -MigrateTrLcd  remove the task of the predecessor TrLcd and take over its theme
#   -WhatIf        show what would change without changing anything
#
# Update: the new files go to "<target>.new" first, the running version is moved to "<target>.bak",
# then "<target>.new" becomes the target. If anything fails, the previous version is moved back
# and its task is started again.
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Source,
    [string]$ResultFile,
    [switch]$Bench,
    [switch]$MigrateTrLcd
)
$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 leaves $PSScriptRoot empty in parameter defaults of a script with
# [CmdletBinding()], so the default is resolved here instead
if (-not $Source) { $Source = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'app' }

$SidSystem = 'S-1-5-18'
$SidAdmins = 'S-1-5-32-544'

# Refuses a folder tree that contains a junction or symbolic link anywhere. Does not descend into
# reparse points, so a cyclic junction cannot loop.
function Assert-NoReparsePoint([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Refusing $Path`: it is a junction or symbolic link. Remove it and run the installer again."
    }
    if ($item.PSIsContainer) {
        foreach ($child in @(Get-ChildItem -LiteralPath $Path -Force)) { Assert-NoReparsePoint $child.FullName }
    }
}

function Get-OwnerSid([string]$Path) {
    (Get-Acl -LiteralPath $Path).GetOwner([Security.Principal.SecurityIdentifier]).Value
}

$failed = $false
$swapped = $false      # set once the previous version may have been moved to $backup
$taskExisted = $false
if ($ResultFile) { Start-Transcript -Path $ResultFile -Force -WhatIf:$false -Confirm:$false | Out-Null }
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
    $staging = "$target.new"
    $backup = "$target.bak"
    $data = Join-Path $env:ProgramData 'UltraLightRgb'
    $startMenu = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'
    $taskExisted = [bool](Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue)

    # Normal users may create folders under ProgramData. A folder that exists before the install must
    # belong to SYSTEM or Administrators and must not contain junctions or symbolic links; otherwise
    # a user could own it (and change its ACL later) or redirect the service's writes.
    if (Test-Path -LiteralPath $data) {
        Assert-NoReparsePoint $data
        $owner = Get-OwnerSid $data
        if ($owner -notin $SidSystem, $SidAdmins) {
            throw "Refusing $data`: owned by $owner, not by SYSTEM or Administrators. Check who created it, delete it and run the installer again."
        }
    }

    # 1. Stage the new version next to the old one; a failed copy leaves the running version untouched
    if ($PSCmdlet.ShouldProcess($staging, 'Copy new version')) {
        if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force -Confirm:$false }
        New-Item -ItemType Directory -Path $staging -Force -Confirm:$false | Out-Null
        Copy-Item -Path (Join-Path $Source '*') -Destination $staging -Recurse -Force -Confirm:$false
    }

    # 2. Stop the service and the switcher: an open switcher window locks its DLL
    if ($PSCmdlet.ShouldProcess($task, 'Stop task and processes')) {
        if ($taskExisted) { Stop-ScheduledTask -TaskName $task }
        Get-Process UltraLightRgb, UltraLightRgb.Switch -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
    }

    # predecessor of this project (TrLcd): only on request; its files stay in place
    if ($MigrateTrLcd -and $PSCmdlet.ShouldProcess('TrLcd', 'Remove task, stop processes, remove shortcut')) {
        if (Get-ScheduledTask -TaskName 'TrLcd' -ErrorAction SilentlyContinue) {
            Stop-ScheduledTask -TaskName 'TrLcd'
            Unregister-ScheduledTask -TaskName 'TrLcd' -Confirm:$false
            'Removed the old TrLcd task (its folder stays, delete it yourself when no longer needed)'
        }
        Get-Process TrLcd, TrLcdSwitch -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
        Remove-Item (Join-Path $startMenu 'TrLcd Themes.lnk') -ErrorAction SilentlyContinue -Confirm:$false
    }
    Start-Sleep -Seconds 1

    # 3. Swap: old -> .bak, .new -> target
    if ($PSCmdlet.ShouldProcess($target, "Replace with $staging (previous version to $backup)")) {
        if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force -Confirm:$false }
        $swapped = $true
        if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination $backup -Confirm:$false }
        Move-Item -LiteralPath $staging -Destination $target -Confirm:$false
    }

    # 4. Data folder. Configuration is read by SYSTEM: only admins may change it. The switcher, running
    # as a normal user, writes one word into state\theme.txt, which the service validates against a
    # fixed list. SIDs instead of names, because localized Windows renames the groups.
    if ($PSCmdlet.ShouldProcess($data, 'Create folders, set owner Administrators, lock down the ACL')) {
        # status: written by the service while the switcher is open, users may only read it
        New-Item -ItemType Directory -Path (Join-Path $data 'state'), (Join-Path $data 'logs'), (Join-Path $data 'status') -Force -Confirm:$false | Out-Null
        Assert-NoReparsePoint $data
        icacls $data /setowner "*$SidAdmins" /T /C | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "icacls $data /setowner failed with exit code $LASTEXITCODE" }
        icacls $data /inheritance:r /grant:r "*${SidSystem}:(OI)(CI)F" "*${SidAdmins}:(OI)(CI)F" '*S-1-5-32-545:(OI)(CI)RX' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "icacls $data failed with exit code $LASTEXITCODE" }
        icacls (Join-Path $data 'state') /grant '*S-1-5-32-545:(OI)(CI)M' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "icacls state failed with exit code $LASTEXITCODE" }
    }

    if ($MigrateTrLcd) {
        $oldTheme = Join-Path $env:ProgramData 'TrLcd\theme.txt'
        $newTheme = Join-Path $data 'state\theme.txt'
        if ((Test-Path $oldTheme) -and -not (Test-Path $newTheme) -and $PSCmdlet.ShouldProcess($newTheme, "Copy theme from $oldTheme")) {
            Copy-Item $oldTheme $newTheme -Confirm:$false
            "Theme taken over from TrLcd: $(Get-Content $newTheme -TotalCount 1)"
        }
    }

    if ($Bench -and $PSCmdlet.ShouldProcess((Join-Path $target 'UltraLightRgb.exe'), 'Run --bench')) {
        & (Join-Path $target 'UltraLightRgb.exe') --bench
        "bench_exit=$LASTEXITCODE"
    }

    $lnk = Join-Path $startMenu 'Ultra Light RGB Control.lnk'
    if ($PSCmdlet.ShouldProcess($lnk, 'Create Start menu shortcut')) {
        $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk)
        $shortcut.TargetPath = Join-Path $target 'UltraLightRgb.Switch.exe'
        $shortcut.WorkingDirectory = $target
        $shortcut.Description = 'Pick the LCD and LED theme'
        $shortcut.Save()
    }

    if (-not $PSCmdlet.ShouldProcess($task, 'Register and start the SYSTEM task')) { return }
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
    $taskExisted = $true
    Start-ScheduledTask -TaskName $task
    Start-Sleep -Seconds 4

    $proc = Get-Process UltraLightRgb -ErrorAction SilentlyContinue
    if (-not $proc) { throw "The task started but UltraLightRgb is not running, see $data\logs\service.log" }

    # success: the previous version is no longer needed
    $swapped = $false
    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force -Confirm:$false }
    "task=$((Get-ScheduledTask -TaskName $task).State) pid=$($proc.Id)"
    "UltraLightRgb.dll SHA256=$((Get-FileHash (Join-Path $target 'UltraLightRgb.dll')).Hash)"
    "Configuration: $data\devices.json (optional, example: $target\devices.example.json)"
    'INSTALL_OK'
}
catch {
    $failed = $true
    "INSTALL_FAILED: $($_.Exception.Message)"
    # Roll back to the previous version and bring its task back up
    if ($swapped -and (Test-Path -LiteralPath $backup)) {
        try {
            Get-Process UltraLightRgb, UltraLightRgb.Switch -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
            Start-Sleep -Seconds 1
            if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force -Confirm:$false }
            Move-Item -LiteralPath $backup -Destination $target -Confirm:$false
            "ROLLBACK_OK: previous version restored to $target"
        }
        catch { "ROLLBACK_FAILED: $($_.Exception.Message) (previous version: $backup)" }
    }
    if ($taskExisted -and (Test-Path (Join-Path $target 'UltraLightRgb.exe'))) {
        try { Start-ScheduledTask -TaskName $task; "Task $task started again" }
        catch { "TASK_RESTART_FAILED: $($_.Exception.Message)" }
    }
}
finally {
    if ($ResultFile) { Stop-Transcript | Out-Null }
}
if ($failed) { exit 1 }
