# AirServer – installateur
#   irm https://raw.githubusercontent.com/mehmouud/airserver/main/install.ps1 | iex
#
# - installe dans %LOCALAPPDATA%\Programs\AirServer (pas besoin d'admin)
# - ajoute AirServer au menu Démarrer + entrée "Applications installées" (désinstallable)
# - demande UAC une seule fois si nécessaire : règles pare-feu (+ réseau Public -> Privé si accepté)
# - lance AirServer
# Fonctionne avec Windows PowerShell 5.1 (PC neuf) et PowerShell 7.

& {
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'          # sinon Invoke-WebRequest est 10x plus lent en 5.1
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$Repo     = 'mehmouud/airserver'
$ZipUrl   = if ($env:AIRSERVER_ZIP) { $env:AIRSERVER_ZIP } else { "https://github.com/$Repo/releases/latest/download/AirServer-win-x64.zip" }
$AppName  = 'AirServer'
$Dir      = Join-Path $env:LOCALAPPDATA "Programs\$AppName"
$Exe      = Join-Path $Dir 'AirServer.exe'
$Engine   = Join-Path $Dir 'engine\uxplay.exe'
$StartLnk = Join-Path ([Environment]::GetFolderPath('Programs')) "$AppName.lnk"
$UninstKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppName"

function Step($t) { Write-Host "==> $t" -ForegroundColor Cyan }
function Ok($t)   { Write-Host "    $t" -ForegroundColor Green }
function Warn($t) { Write-Host "    $t" -ForegroundColor Yellow }

Write-Host ''
Write-Host '  AirServer – recopie d''écran iPhone pour Windows' -ForegroundColor White
Write-Host ''

if (-not [Environment]::Is64BitOperatingSystem -or [Environment]::OSVersion.Version.Major -lt 10) {
    Write-Host 'Windows 10/11 64 bits requis.' -ForegroundColor Red; return
}

# 1. fermer une version déjà lancée (mise à jour)
Step 'Préparation'
Get-Process AirServer, uxplay -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Dir*" } | ForEach-Object {
    $_.CloseMainWindow() | Out-Null; if (-not $_.WaitForExit(3000)) { $_ | Stop-Process -Force }
}

# 2. téléchargement + vérification
Step 'Téléchargement'
$tmp = Join-Path $env:TEMP ("airserver-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
$zip = Join-Path $tmp 'AirServer-win-x64.zip'
try {
    if (Test-Path $ZipUrl) { Copy-Item $ZipUrl $zip } else { Invoke-WebRequest -UseBasicParsing -Uri $ZipUrl -OutFile $zip }
    $shaSrc = "$ZipUrl.sha256"
    $expected = if (Test-Path $shaSrc) { Get-Content $shaSrc -Raw } else { (Invoke-WebRequest -UseBasicParsing -Uri $shaSrc).Content }
    if ($expected -is [byte[]]) { $expected = [Text.Encoding]::ASCII.GetString($expected) }
    $expected = ($expected -split '\s+')[0].Trim().ToLower()
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
    if ($expected -ne $actual) { throw "Somme SHA-256 invalide (attendu $expected, obtenu $actual)" }
    Ok ("{0:N0} Mo, SHA-256 vérifié" -f ((Get-Item $zip).Length / 1MB))

    # 3. installation
    Step "Installation dans $Dir"
    if (Test-Path $Dir) { Remove-Item $Dir -Recurse -Force }
    New-Item -ItemType Directory -Path $Dir | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $Dir)
    if (-not (Test-Path $Exe) -or -not (Test-Path $Engine)) { throw 'Archive incomplète' }
    Ok 'Fichiers installés'
} finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

# 4. menu Démarrer + désinstallation
Step 'Menu Démarrer'
$ws = New-Object -ComObject WScript.Shell
$l = $ws.CreateShortcut($StartLnk)
$l.TargetPath = $Exe; $l.WorkingDirectory = $Dir; $l.IconLocation = "$Exe,0"
$l.Description = 'Recopie d''écran iPhone (AirPlay)'
$l.Save()
$version = (Get-Item $Exe).VersionInfo.ProductVersion
New-Item -Path $UninstKey -Force | Out-Null
$uninstCmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$Dir\uninstall.ps1`""
@{ DisplayName = $AppName; DisplayVersion = "$version"; Publisher = 'mehmouud'; DisplayIcon = "$Exe,0"
   InstallLocation = $Dir; UninstallString = $uninstCmd; QuietUninstallString = "$uninstCmd -Quiet"
   URLInfoAbout = "https://github.com/$Repo" }.GetEnumerator() | ForEach-Object {
    New-ItemProperty -Path $UninstKey -Name $_.Key -Value $_.Value -PropertyType String -Force | Out-Null }
New-ItemProperty -Path $UninstKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $UninstKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
Ok 'AirServer ajouté au menu Démarrer et aux Applications installées'

# 5. réseau + pare-feu (admin, une seule demande UAC si besoin)
Step 'Réseau et pare-feu'
$admin = @()
$hasRule = $false
try {
    $hasRule = [bool](Get-NetFirewallApplicationFilter -Program $Engine -ErrorAction SilentlyContinue |
        Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object { $_.Enabled -eq 'True' -and $_.Action -eq 'Allow' -and $_.Direction -eq 'Inbound' })
} catch { }
if (-not $hasRule) {
    $admin += "Get-NetFirewallRule -DisplayName 'AirServer*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule"
    foreach ($proto in 'TCP', 'UDP') {
        $admin += "New-NetFirewallRule -DisplayName 'AirServer ($proto)' -Direction Inbound -Action Allow -Program '$Engine' -Protocol $proto -Profile Private,Domain | Out-Null"
    }
}
$public = @(Get-NetConnectionProfile -ErrorAction SilentlyContinue | Where-Object { $_.NetworkCategory -eq 'Public' -and $_.IPv4Connectivity -ne 'Disconnected' })
foreach ($n in $public) {
    Warn "Le réseau « $($n.Name) » ($($n.InterfaceAlias)) est en mode Public : l'iPhone ne verra pas ce PC."
    $ans = Read-Host '    Le passer en réseau Privé ? (à faire uniquement sur un réseau de confiance, ex. maison) [O/n]'
    if ($ans -notmatch '^(n|non|no)$') { $admin += "Set-NetConnectionProfile -InterfaceIndex $($n.InterfaceIndex) -NetworkCategory Private" }
}
if ($admin.Count -gt 0) {
    Warn 'Windows va demander l''autorisation administrateur (pare-feu / réseau)...'
    $script = ($admin -join "`n")
    $enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
    try {
        $p = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -WindowStyle Hidden -ArgumentList '-NoProfile', '-EncodedCommand', $enc
        if ($p.ExitCode -eq 0) { Ok 'Pare-feu / réseau configurés' } else { Warn "La configuration admin a renvoyé le code $($p.ExitCode)" }
    } catch {
        Warn 'Autorisation refusée : Windows demandera l''accès réseau au premier lancement (cocher « Réseaux privés »).'
    }
} else {
    Ok 'Déjà configuré'
}

# 6. lancement
Step 'Lancement'
Start-Process -FilePath $Exe -WorkingDirectory $Dir
Ok 'AirServer est lancé. Sur l''iPhone : Centre de contrôle > Recopie de l''écran > ce PC.'
Write-Host '    (relancer plus tard : menu Démarrer > AirServer)'
Write-Host ''
}
