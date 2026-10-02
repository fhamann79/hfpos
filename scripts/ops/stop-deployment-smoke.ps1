$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $PSScriptRoot 'smoke-support.ps1')
Invoke-SmokeResourceCleanup -Root $root
