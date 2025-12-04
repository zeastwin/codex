#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WINROOT="$(wslpath -w "$ROOT")"

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \
  "Set-Location '$WINROOT'; powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run-assistant.ps1"
