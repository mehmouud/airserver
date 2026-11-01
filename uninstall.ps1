# AirServer – désinstallation (appelé depuis Paramètres > Applications installées)
param([switch]$Quiet)
$ErrorActionPreference = 'SilentlyContinue'
$Dir = Join-Path $env:LOCALAPPDATA 'Programs\AirServer'
if (-not $Quiet) {
    Add-Type -AssemblyName System.Windows.Forms
    $r = [System.Windows.Forms.MessageBox]::Show('Désinstaller AirServer ?', 'AirServer', 'YesNo', 'Question')
    if ($r -ne 'Yes') { exit 1 }
}
Get-Process AirServer, uxplay | Where-Object { $_.Path -like "$Dir*" } | Stop-Process -Force
Start-Sleep -Milliseconds 500
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'AirServer.lnk') -Force
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AirServer' -Recurse -Force
Remove-Item (Join-Path $env:APPDATA 'AirServer') -Recurse -Force
Remove-Item (Join-Path $env:LOCALAPPDATA 'AirServer') -Recurse -Force
# Firewall rules need admin: best effort, only if some exist
if (Get-NetFirewallRule -DisplayName 'AirServer*') {
    Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -Wait -ArgumentList '-NoProfile', '-Command',
        "Get-NetFirewallRule -DisplayName 'AirServer*' | Remove-NetFirewallRule"
}
# the script lives inside $Dir: delete the folder from a detached process after we exit
Start-Process cmd.exe -WindowStyle Hidden -ArgumentList '/c', "timeout /t 2 >nul & rmdir /s /q `"$Dir`""
