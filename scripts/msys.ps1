# Run a bash script inside the MSYS2 UCRT64 environment.
# Usage: powershell -File scripts\msys.ps1 <script.sh> [args...]
param([Parameter(Mandatory=$true)][string]$Script, [Parameter(ValueFromRemainingArguments=$true)]$Rest)
$env:MSYSTEM = 'UCRT64'
$env:CHERE_INVOKING = '1'
$unix = (& C:\msys64\usr\bin\cygpath.exe -u (Resolve-Path $Script).Path)
& C:\msys64\usr\bin\bash.exe -l $unix @Rest
exit $LASTEXITCODE
