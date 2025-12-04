# tools/run-assistant.ps1
param(
  [string]$Exe = ".\EW-Assistant\bin\Debug\EW-Assistant.exe"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repoRoot

if (-not (Test-Path $Exe)) {
  throw "Exe not found: $Exe"
}

Start-Process $Exe
