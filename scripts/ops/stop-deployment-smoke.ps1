$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$runtime = [IO.Path]::GetFullPath((Join-Path $root 'deploy/compose/smoke-runtime'))
$expected = [IO.Path]::GetFullPath((Join-Path $root 'deploy/compose')) + [IO.Path]::DirectorySeparatorChar
if (!$runtime.StartsWith($expected, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe cleanup target.' }
$envFile = Join-Path $runtime 'smoke.env'
if (Test-Path -LiteralPath $envFile) {
    $result = & docker compose --project-name hfpos-520-smoke --env-file $envFile -f (Join-Path $root 'deploy/compose/compose.smoke.yml') down -v --remove-orphans 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Smoke resource cleanup failed; runtime preserved for retry.' }
    Remove-Item -LiteralPath $runtime -Recurse -Force
    Write-Host 'SMOKE CLEANUP PASS'
}
