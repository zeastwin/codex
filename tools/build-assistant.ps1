# tools/build-assistant.ps1
param(
  [string]$Configuration = "Debug",
  [string]$Platform = "Any CPU"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$assistantDir = Join-Path $repoRoot "EW-Assistant"
$sln = Join-Path $assistantDir "EW-Assistant.sln"

if (-not (Test-Path $sln)) { throw "Solution not found: $sln" }

function Ensure-Dir([string]$p) { if (-not (Test-Path $p)) { New-Item -ItemType Directory -Path $p | Out-Null } }

function Find-MSBuild {
  $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
  if (-not (Test-Path $vswhere)) { throw "vswhere not found: $vswhere (install VS/Build Tools)" }

  $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
  if ([string]::IsNullOrWhiteSpace($msbuild) -or -not (Test-Path $msbuild)) { throw "MSBuild.exe not found (install MSBuild workload / Build Tools)" }
  return $msbuild
}

function Extract-Errors([string]$logPath, [string]$outPath) {
  if (-not (Test-Path $logPath)) { "No msbuild.log found: $logPath" | Set-Content -Encoding UTF8 $outPath; return }

  $lines = Get-Content -LiteralPath $logPath -Encoding UTF8
  $regex = [regex]'(?i)\berror\s+([A-Z]{2,}\d+|MSB\d+)\s*:|\bfatal error\b'

  $hits = New-Object System.Collections.Generic.List[int]
  for ($i = 0; $i -lt $lines.Count; $i++) { if ($regex.IsMatch($lines[$i])) { $hits.Add($i) } }

  $sb = New-Object System.Text.StringBuilder
  [void]$sb.AppendLine("Build Errors Extract: " + (Get-Date).ToString("yyyy-MM-dd HH:mm:ss"))
  [void]$sb.AppendLine("Log: $logPath")
  [void]$sb.AppendLine("")

  if ($hits.Count -eq 0) {
    [void]$sb.AppendLine("No 'error' lines matched. Check full msbuild.log.")
    $sb.ToString() | Set-Content -Encoding UTF8 $outPath
    return
  }

  $take = [Math]::Min(5, $hits.Count)
  for ($k = 0; $k -lt $take; $k++) {
    $i = $hits[$k]
    $start = [Math]::Max(0, $i - 2)
    $end = [Math]::Min($lines.Count - 1, $i + 2)
    [void]$sb.AppendLine(("---- Error #{0} (line {1}) ----" -f ($k + 1), ($i + 1)))
    for ($j = $start; $j -le $end; $j++) { [void]$sb.AppendLine($lines[$j]) }
    [void]$sb.AppendLine("")
  }

  $sb.ToString() | Set-Content -Encoding UTF8 $outPath
}

$artifacts = Join-Path $repoRoot "artifacts\assistant"
Ensure-Dir $artifacts

$binlog = Join-Path $artifacts "msbuild.binlog"
$log    = Join-Path $artifacts "msbuild.log"
$errors = Join-Path $artifacts "errors.txt"

# 每次构建前清理关键产物
Remove-Item -LiteralPath $binlog, $log, $errors -ErrorAction SilentlyContinue

$msbuild = Find-MSBuild
Write-Host "MSBuild: $msbuild"
Write-Host "Solution: $sln"
Write-Host "Config: $Configuration | Platform: $Platform"
Write-Host ""

Push-Location $assistantDir
try {
  & $msbuild $sln /m /t:Restore;Build `
    /p:Configuration=$Configuration `
    /p:Platform="$Platform" `
    /v:m /nologo `
    /bl:$binlog `
    /fileLogger /fileLoggerParameters:"LogFile=$log;Verbosity=normal"

  $exit = $LASTEXITCODE
}
finally {
  Pop-Location
}

if ($exit -ne 0) {
  Write-Host "Build FAILED (exit=$exit). Writing errors to: $errors"
  Extract-Errors -logPath $log -outPath $errors
  exit $exit
}

Write-Host "Build OK. Log: $log"
exit 0
