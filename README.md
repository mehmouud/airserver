# AirServer (Windows) – recopie d'écran iPhone

Récepteur AirPlay Mirroring pour Windows 10/11 x64 : interface .NET (`src/AirServer`) + moteur
[UxPlay](https://github.com/FDH2/UxPlay) 1.74 (GPLv3) compilé avec MSYS2/GStreamer, décodage GPU D3D11/D3D12.

## Installation (une ligne, PowerShell)
```powershell
irm https://raw.githubusercontent.com/mehmouud/airserver/main/install.ps1 | iex
```
Le script :
1. télécharge la dernière release et vérifie sa somme SHA-256 ;
2. installe dans `%LOCALAPPDATA%\Programs\AirServer` (sans droits admin) ;
3. ajoute **AirServer au menu Démarrer** et aux *Applications installées* (désinstallable) ;
4. demande l'autorisation admin (UAC) **uniquement si nécessaire** : règles pare-feu, et passage du réseau Public → Privé si vous l'acceptez ;
5. lance AirServer.

Relancer la même commande = mise à jour.

## Utilisation
iPhone : Centre de contrôle → Recopie de l'écran → nom du PC. Alt+Entrée dans la fenêtre vidéo = plein écran.
L'iPhone doit être sur le même réseau que le PC (profil Windows **Privé**) ou connecté au **point d'accès mobile** du PC.

- Réglages (⚙) : nom du PC, qualité (Maximale / Équilibrée / Compatibilité), fluidité, son, plein écran, démarrage.
- Informations techniques (journal, statistiques, réseau) : **Réglages → Options avancées**, ou **Ctrl+Maj+D**.
- `AirServer.exe --demo` : simule un iPhone pour tester l'interface sans appareil (ne modifie pas les réglages).

## Qualité : ce qui est réellement contrôlable
- **Débit** : choisi par l'encodeur de l'iPhone, qui s'adapte en continu au réseau. Aucun récepteur AirPlay ne le pilote.
- **Résolution** : le récepteur *demande* une taille (`-s`), l'iPhone décide. Par défaut : HEVC 3840×2160, 60 i/s.
- L'interface affiche la géométrie **source** annoncée par l'iPhone et la géométrie **reçue**, plus les rapports 1 Hz de l'iPhone.

## Build depuis les sources
```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1
```
Installe MSYS2 si besoin, récupère UxPlay au commit testé, applique `patches/`, compile, vérifie les 39 éléments
GStreamer requis, publie l'app autonome et produit `release\AirServer-win-x64.zip` (+ `.sha256`).

## Licence
GPLv3 (comme UxPlay). Sources d'UxPlay : https://github.com/FDH2/UxPlay (commit `f40b121`) + `patches/`.
