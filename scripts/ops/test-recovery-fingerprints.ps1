param([Parameter(Mandatory = $true)][ValidatePattern('^hfpos-dev531-sequences-[a-f0-9]{12}$')][string]$Container)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'smoke-support.ps1')
$context = if ($env:OS -eq 'Windows_NT') { 'desktop-linux' } else { 'default' }
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'start-deployment-smoke.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw 'Recovery script has parser errors.' }
foreach ($name in @('Check', 'BusinessFingerprint', 'Invoke-SmokeSequenceNegatives')) {
    $definition = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    if ($definition) { . ([scriptblock]::Create($definition.Extent.Text)) }
}
function Compose([string[]]$Arguments) {
    $service = [Array]::IndexOf($Arguments, 'postgres')
    Check (($Arguments[0..1] -join ' ') -eq 'exec -T' -and $service -ge 2) 'Unexpected fixture command.'
    $options = if ($service -gt 2) { @($Arguments[2..($service - 1)]) } else { @() }
    $native = Invoke-SmokeNativeCommand -FilePath 'docker' -Arguments (@('--context', $context, 'exec') + $options + @($Container) + $Arguments[($service + 1)..($Arguments.Count - 1)])
    Check ($native.ExitCode -eq 0) 'Isolated fingerprint SQL failed.'
    return $native.Output
}
# The caller owns this fresh container. Never connect to a preview/shared database.
$setup = @'
CREATE TABLE "Sales" ("Id" bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, amount numeric);
INSERT INTO "Sales" (amount) VALUES (10), (20);
CREATE SCHEMA "synthetic.schema";
CREATE SEQUENCE "synthetic.schema"."quoted sequence";
DO $fixture$
DECLARE name text;
BEGIN
  FOREACH name IN ARRAY ARRAY['__EFMigrationsHistory','Companies','Users','PlatformUsers','SaleItems','ProductStocks','Products','CashSessions','PurchaseReceipts','CreditNotes']
  LOOP EXECUTE format('CREATE TABLE %I (id integer)', name); END LOOP;
END $fixture$;
'@
$null = Compose @('exec', '-T', 'postgres', 'psql', '-X', '-qAt', '-v', 'ON_ERROR_STOP=1', '-d', 'hfpos_ops_ci', '-c', $setup)
$clone = Invoke-SmokeNativeCommand -FilePath 'docker' -Arguments @('--context', $context, 'exec', $Container, 'sh', '-c', 'set -eu; pg_dump -Fc -f /tmp/synthetic.dump hfpos_ops_ci; createdb hfpos_ops_restore; pg_restore --exit-on-error -d hfpos_ops_restore /tmp/synthetic.dump')
Check ($clone.ExitCode -eq 0) 'Isolated sequence fixture restore failed.'
$before = BusinessFingerprint 'hfpos_ops_ci'
$rows = BusinessFingerprint 'hfpos_ops_restore' -TablesOnly
Check ($before -eq (BusinessFingerprint 'hfpos_ops_restore')) 'Exact sequence restore must initially match.'
$null = Compose @('exec', '-T', 'postgres', 'psql', '-X', '-qAt', '-v', 'ON_ERROR_STOP=1', '-d', 'hfpos_ops_restore', '-c', 'SELECT setval(''"Sales_Id_seq"'', 1, true);')
Check ($before -ne (BusinessFingerprint 'hfpos_ops_restore')) 'last_value-only corruption was not detected despite identical rows.'
Check ($rows -eq (BusinessFingerprint 'hfpos_ops_restore' -TablesOnly)) 'last_value negative changed rows.'
$null = Compose @('exec', '-T', 'postgres', 'psql', '-X', '-qAt', '-v', 'ON_ERROR_STOP=1', '-d', 'hfpos_ops_restore', '-c', 'SELECT setval(''"Sales_Id_seq"'', 2, false);')
Check ($before -ne (BusinessFingerprint 'hfpos_ops_restore')) 'is_called-only corruption was not detected despite identical rows.'
Check ($rows -eq (BusinessFingerprint 'hfpos_ops_restore' -TablesOnly)) 'is_called negative changed rows.'
$null = Compose @('exec', '-T', 'postgres', 'psql', '-X', '-qAt', '-v', 'ON_ERROR_STOP=1', '-d', 'hfpos_ops_restore', '-c', 'SELECT setval(''"Sales_Id_seq"'', 2, true);')
Check ($before -eq (BusinessFingerprint 'hfpos_ops_restore')) 'Restored logical state did not return to equality.'
Check ($before -eq (BusinessFingerprint 'hfpos_ops_ci')) 'Sequence negatives changed the source fixture.'
$null = Compose @('exec', '-T', 'postgres', 'sh', '/ops/postgres-backup.sh', '/backups')
$backup = (Compose @('exec', '-T', 'postgres', 'sh', '-c', 'ls /backups/*.dump')).Trim()
Invoke-SmokeSequenceNegatives -Backup $backup
Write-Host "RECOVERY FINGERPRINT TESTS PASS last_value/is_called/source-readonly PowerShell=$($PSVersionTable.PSVersion)"
