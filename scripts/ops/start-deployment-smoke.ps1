param([switch]$Cleanup, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'smoke-support.ps1')
$windowsPlatform = $IsWindows -or $env:OS -eq 'Windows_NT'
$curlExecutable = (Get-Command -Name $(if ($windowsPlatform) { 'curl.exe' } else { 'curl' }) -CommandType Application | Select-Object -First 1).Source
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$runtime = Join-Path $root 'deploy/compose/smoke-runtime'
$compose = Join-Path $root 'deploy/compose/compose.smoke.yml'
$envFile = Join-Path $runtime 'smoke.env'
$identity = Get-SmokeIdentity -Root $root
if (Test-Path -LiteralPath $runtime) { throw 'Smoke runtime already exists. Run stop-deployment-smoke.ps1 first.' }
$watch = [Diagnostics.Stopwatch]::StartNew()
$success = $false
$smokeError = $null

function DockerCommand([string[]]$Arguments, [switch]$ExpectFailure) {
    $native = Invoke-SmokeNativeCommand -FilePath 'docker' -Arguments (@('--context', $identity.Context) + $Arguments)
    $result = $native.Output
    $code = $native.ExitCode
    if ($ExpectFailure) {
        if ($code -eq 0) { throw 'Expected safety refusal did not occur.' }
    } elseif ($code -ne 0) {
        $safeMessage = if (($result -join "`n") -match '(PILOT PROOF FAILED step=[A-Za-z0-9/._-]+ type=[A-Za-z0-9]+)') { $Matches[1] }
            elseif (($result -join "`n") -match '(Migration (listing )?failed[^\r\n]*)') { $Matches[1] } else { 'No secret-bearing output emitted.' }
        throw "Docker operation failed: $($Arguments[0]). $safeMessage"
    }
    return ($result -join "`n")
}
function Compose([string[]]$Arguments, [switch]$ExpectFailure) {
    try { DockerCommand -Arguments (@('compose', '--project-name', $identity.Project, '--env-file', $envFile, '-f', $compose) + $Arguments) -ExpectFailure:$ExpectFailure }
    catch { throw "Smoke compose step failed: $($Arguments[0]); $($_.Exception.Message)" }
}
function Check([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function SourceFingerprint {
    # Stage the dump so a failed pg_dump cannot be hidden by a successful hash pipeline.
    Compose -Arguments @('exec', '-T', 'postgres', 'sh', '-c', 'set -eu; file=$(mktemp); trap ''rm -f "$file"'' EXIT; pg_dump --data-only --no-owner --no-privileges "$PGDATABASE" > "$file"; sed "/^\\\\restrict /d; /^\\\\unrestrict /d" "$file" | sha256sum')
}
function BusinessFingerprint {
    param([ValidateSet('hfpos_ops_ci', 'hfpos_ops_restore', 'hfpos_ops_sequence_negative')][string]$Database, [switch]$TablesOnly)
    # Canonical JSON rows avoid cross-database OID/table/physical row ordering differences.
    $query = @'
CREATE TEMP TABLE smoke_hashes (name text, hash text);
DO $proof$
DECLARE relation record; content_hash text;
BEGIN
  FOR relation IN SELECT n.nspname, c.relname, c.relkind FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
      AND (c.relkind='r' OR (c.relkind='S' AND __INCLUDE_SEQUENCES__))
  LOOP
    IF relation.relkind='S' THEN
      -- Logical next-insert state only: log_cnt is internal WAL/preallocation state.
      EXECUTE format('SELECT md5(jsonb_build_array(last_value, is_called)::text) FROM %I.%I', relation.nspname, relation.relname) INTO content_hash;
    ELSE
      EXECUTE format('SELECT md5(coalesce(string_agg(to_jsonb(row)::text, chr(10) ORDER BY to_jsonb(row)::text), '''')) FROM %I.%I row', relation.nspname, relation.relname) INTO content_hash;
    END IF;
    INSERT INTO smoke_hashes VALUES (jsonb_build_array(relation.nspname, relation.relname)::text, content_hash);
  END LOOP;
END $proof$;
SELECT md5(string_agg(name || ':' || hash, chr(10) ORDER BY name COLLATE "C")) FROM smoke_hashes;
'@
    $query = $query.Replace('__INCLUDE_SEQUENCES__', $(if ($TablesOnly) { 'false' } else { 'true' }))
    $hash = Compose -Arguments @('exec', '-T', 'postgres', 'psql', '-X', '-qAt', '-v', 'ON_ERROR_STOP=1', '-d', $Database, '-c', $query)
    Check ($hash.Trim() -match '^[a-f0-9]{32}$') 'Business fingerprint result invalid.'
    return $hash.Trim()
}
function Invoke-SmokeSequenceNegatives([string]$Backup) {
    # A third disposable DB owns every setval; source and preview restore stay read-only.
    $database = 'hfpos_ops_sequence_negative'
    $source = BusinessFingerprint 'hfpos_ops_ci'
    $null = Compose @('exec', '-T', 'postgres', 'createdb', '-U', 'hfpos_smoke', $database)
    $null = Compose @('exec', '-T', '-e', "PGDATABASE=$database", '-e', 'HFPOS_RESTORE_APPROVED=YES', 'postgres', 'sh', '/ops/postgres-restore.sh', $Backup)
    Check ($source -eq (BusinessFingerprint $database)) 'Sequence negative clone does not match source.'
    $rows = BusinessFingerprint $database -TablesOnly
    $query = @'
CREATE TEMP TABLE sequence_changes (payload json);
DO $proof$
DECLARE relation record; state record;
BEGIN
  SELECT n.nspname, c.relname, s.seqmin, s.seqmax INTO STRICT relation
    FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace JOIN pg_sequence s ON s.seqrelid=c.oid
    WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
    ORDER BY n.nspname COLLATE "C", c.relname COLLATE "C" LIMIT 1;
  EXECUTE format('SELECT last_value, is_called FROM %I.%I', relation.nspname, relation.relname) INTO STRICT state;
  INSERT INTO sequence_changes VALUES (json_build_object(
    'valueChange', format('SELECT setval(%L::regclass, %s, %L);', format('%I.%I', relation.nspname, relation.relname),
      CASE WHEN state.last_value < relation.seqmax THEN state.last_value + 1 ELSE state.last_value - 1 END, state.is_called),
    'calledChange', format('SELECT setval(%L::regclass, %s, %L);', format('%I.%I', relation.nspname, relation.relname), state.last_value, NOT state.is_called),
    'reset', format('SELECT setval(%L::regclass, %s, %L);', format('%I.%I', relation.nspname, relation.relname), state.last_value, state.is_called)));
END $proof$;
SELECT payload FROM sequence_changes;
'@
    $changes = Compose @('exec', '-T', 'postgres', 'psql', '-X', '-qAt', '-v', 'ON_ERROR_STOP=1', '-d', $database, '-c', $query) | ConvertFrom-Json
    foreach ($change in @($changes.valueChange, $changes.calledChange)) {
        $null = Compose @('exec', '-T', 'postgres', 'psql', '-X', '-qAt', '-v', 'ON_ERROR_STOP=1', '-d', $database, '-c', $change)
        Check ($rows -eq (BusinessFingerprint $database -TablesOnly)) 'Sequence negative changed business rows.'
        Check ($source -ne (BusinessFingerprint $database)) 'Sequence corruption passed recovery fingerprint.'
        $null = Compose @('exec', '-T', 'postgres', 'psql', '-X', '-qAt', '-v', 'ON_ERROR_STOP=1', '-d', $database, '-c', $changes.reset)
        Check ($source -eq (BusinessFingerprint $database)) 'Sequence negative reset failed.'
    }
    Check ($source -eq (BusinessFingerprint 'hfpos_ops_ci')) 'Sequence negatives changed source.'
    $null = Compose @('exec', '-T', 'postgres', 'dropdb', '-U', 'hfpos_smoke', $database)
    Write-Host 'SEQUENCE LAST_VALUE/IS_CALLED NEGATIVES PASS (rows unchanged, source read-only)'
}
function Invoke-SmokeHttps([string]$Path, [string[]]$Extra = @(), [int]$Port = 8444) {
    if ($windowsPlatform) { $Extra = @($Extra | ForEach-Object { if ($_ -eq '/dev/null') { 'NUL' } else { $_ } }) }
    $result = Invoke-SmokeNativeCommand -FilePath $curlExecutable -Arguments (@('-ksS', '--max-time', '15') + $Extra + @("https://localhost:$Port$Path"))
    if ($result.ExitCode -ne 0) { throw 'Synthetic HTTPS probe failed.' }
    return $result.Output
}

try {
    if ($windowsPlatform) {
        $endpoint = DockerCommand -Arguments @('context', 'inspect', '--format', '{{.Endpoints.docker.Host}}')
        Check ($endpoint.Trim() -eq 'npipe:////./pipe/dockerDesktopLinuxEngine') 'Rehearsal requires the verified local Docker named pipe.'
    }
    Check ([string]::IsNullOrWhiteSpace((DockerCommand -Arguments @('ps', '-aq', '--filter', "label=com.docker.compose.project=$($identity.Project)")))) 'Owned rehearsal project already exists; refusing reuse.'
    $null = New-Item -ItemType Directory -Path (Join-Path $runtime 'tls') -Force
    # Generated credentials are TEST/SMOKE ONLY, NEVER PRODUCTION.
    $pg = [Guid]::NewGuid().ToString('N')
    $jwt = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    $platform = 'Smoke-only-' + [Guid]::NewGuid().ToString('N')
    [IO.File]::WriteAllText($envFile, "SMOKE_PROJECT=$($identity.Project)`nSMOKE_PG_PASSWORD=$pg`nSMOKE_JWT_KEY=$jwt`nSMOKE_PLATFORM_PASSWORD=$platform`n", [Text.UTF8Encoding]::new($false))
    $restoredTemplate = (Get-Content -LiteralPath (Join-Path $root 'deploy/nginx/default.conf.template') -Raw).Replace('http://backend:8080', 'http://restored-backend:8080')
    [IO.File]::WriteAllText((Join-Path $runtime 'restored.conf.template'), $restoredTemplate, [Text.UTF8Encoding]::new($false))
    $null = Compose -Arguments @('config', '--quiet')
    $productionCompose = Join-Path $root 'deploy/compose/compose.production.example.yml'
    $null = DockerCommand -Arguments @('compose', '--env-file', (Join-Path $root 'deploy/compose/.env.production.example'), '-f', $productionCompose, 'config', '--quiet') -ExpectFailure
    $productionEnv = Join-Path $runtime 'production-config.env'
    $configurationOnly = "HFPOS_BACKEND_IMAGE=hfpos/backend:smoke`nHFPOS_WEB_IMAGE=hfpos/web:smoke`nHFPOS_MIGRATIONS_IMAGE=hfpos/migrations:smoke`nHFPOS_RELEASE_VERSION=smoke`nHFPOS_DATABASE_CONNECTION=Host=postgres;Database=hfpos_ops_ci;Username=hfpos_smoke;Password=$pg`nHFPOS_JWT_KEY=$jwt`nHFPOS_JWT_ISSUER=smoke`nHFPOS_JWT_AUDIENCE=smoke`nHFPOS_ALLOWED_HOSTS=localhost`nHFPOS_HEALTH_HOST=localhost`nHFPOS_KEYRING_PATH=$runtime/keys`nHFPOS_TLS_PATH=$runtime/tls`n"
    [IO.File]::WriteAllText($productionEnv, $configurationOnly, [Text.UTF8Encoding]::new($false))
    # Parse configuration in memory only: docker compose config contains runtime secrets.
    $reference = DockerCommand -Arguments @('compose', '--env-file', $productionEnv, '-f', $productionCompose, '--profile', 'migration', 'config', '--format', 'json') | ConvertFrom-Json
    Check (!$reference.services.backend.ports) 'Production reference exposes backend.'
    Check (!$reference.services.postgres) 'Production reference must use external PostgreSQL.'
    Check ($reference.services.migrations.profiles -contains 'migration') 'Migration must be opt-in.'
    Write-Host 'PRODUCTION REFERENCE CONFIG PASS'
    if (!$SkipBuild) {
        $null = Compose -Arguments @('--profile', 'migration', '--profile', 'proof', 'build', 'backend', 'web', 'migrations', 'pilot-proof')
        Write-Host 'IMAGES BUILD PASS'
    }
    $tls = Join-Path $runtime 'tls'
    $null = DockerCommand -Arguments @('run', '--rm', '-v', "${tls}:/tls", 'postgres:16-alpine@sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea', 'sh', '-c',
        'apk add --no-cache openssl >/dev/null 2>&1 && openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj /CN=localhost -addext subjectAltName=DNS:localhost -keyout /tls/private.key -out /tls/certificate.pem >/dev/null 2>&1 && chmod 644 /tls/private.key /tls/certificate.pem')
    $null = Compose -Arguments @('up', '-d', '--wait', 'postgres')
    Write-Host 'SYNTHETIC POSTGRES READY'
    # No approval must fail before database access.
    $null = Compose -Arguments @('run', '--rm', '--no-deps', 'migrations') -ExpectFailure
    Write-Host 'MIGRATION GUARD PASS'
    $migration = Compose -Arguments @('run', '--rm', '--no-deps', '-e', 'HFPOS_MIGRATION_APPROVED=YES', 'migrations')
    Check ($migration.Contains('MIGRATION PASS')) 'Migration result missing.'
    Write-Host 'MIGRATION PASS'
    $null = Compose -Arguments @('up', '-d', '--wait', '--wait-timeout', '120', 'backend', 'web')
    Write-Host 'BACKEND READY'
    foreach ($service in @('backend', 'web', 'migrations')) {
        $image = "$($identity.Project)/${service}:smoke"
        $user = DockerCommand -Arguments @('image', 'inspect', '--format', '{{.Config.User}}', $image)
        Check ($user -ne '' -and $user -notmatch '^(root|0)(:|$)') 'Image must be non-root.'
    }
    $backendId = Compose -Arguments @('ps', '-q', 'backend')
    $ports = DockerCommand -Arguments @('inspect', '--format', '{{json .HostConfig.PortBindings}}', $backendId.Trim())
    Check ($ports -in @('{}', 'null')) 'Backend must not publish host ports.'
    Wait-SmokeWebReady -Probe { param($seconds) (Invoke-SmokeHttps '/' -Extra @('--max-time', "$seconds")) -match '<app-root' }
    Write-Host 'SYNTHETIC WEB READY'
    Check ((Invoke-SmokeHttps '/platform/tenants') -match '<app-root') 'SPA deep link failed.'
    Check ((Invoke-SmokeHttps '/missing-asset.js' -Extra @('-o', '/dev/null', '-w', '%{http_code}')) -eq '404') 'Missing asset must return 404.'
    Check ((Invoke-SmokeHttps '/api/missing' -Extra @('-o', '/dev/null', '-w', '%{http_code}')) -eq '404') 'API must not fall back to Angular.'
    $indexHeaders = Invoke-SmokeHttps '/index.html' -Extra @('-I')
    Check ($indexHeaders -match 'no-cache, must-revalidate') 'Index caching contract failed.'
    $asset = [regex]::Match((Invoke-SmokeHttps '/'), 'src="([^\"]+-[A-Za-z0-9_-]{8,}\.js)"').Groups[1].Value
    Check (![string]::IsNullOrEmpty($asset)) 'Hashed JavaScript asset missing.'
    Check ((Invoke-SmokeHttps "/$asset" -Extra @('-I')) -match 'immutable') 'Hashed asset cache failed.'
    Check (((Invoke-SmokeHttps '/health/live' -Extra @('-H', 'X-Forwarded-Proto: http') | ConvertFrom-Json).status) -eq 'Healthy') 'Proxy must replace forwarded proto.'
    # Synthetic bytes only, NOT a certificate. Verify edge does not reject backend-admissible sizes.
    $body = Join-Path $runtime 'large-body.bin'
    [IO.File]::WriteAllBytes($body, [byte[]]::new(2 * 1024 * 1024))
    Check ((Invoke-SmokeHttps '/api/missing' -Extra @('-X', 'POST', '--data-binary', "@$body", '-o', '/dev/null', '-w', '%{http_code}')) -ne '413') 'Proxy reduced the existing multipart size contract.'
    [IO.File]::WriteAllBytes($body, [byte[]]::new(4 * 1024 * 1024))
    Check ((Invoke-SmokeHttps '/api/missing?password=SMOKE-QUERY-MARKER' -Extra @('-X', 'POST', '--data-binary', "@$body", '-o', '/dev/null', '-w', '%{http_code}')) -eq '413') 'Oversized edge request must be refused.'
    $redirect = Invoke-SmokeNativeCommand -FilePath $curlExecutable -Arguments @('-sS', '-I', '--max-time', '10', 'http://localhost:8084/')
    Check ($redirect.ExitCode -eq 0 -and $redirect.Output -match '308' -and $redirect.Output -match 'https://localhost:8444') 'HTTP edge redirect failed.'
    foreach ($path in @('/health/live', '/health/ready')) {
        $health = Invoke-SmokeHttps $path | ConvertFrom-Json
        Check ($health.status -eq 'Healthy') 'Health check failed.'
    }
    Write-Host 'WEB PASS'
    $loginPath = Join-Path $runtime 'login.json'
    [IO.File]::WriteAllText($loginPath, (@{ username = 'smoke-platform'; password = $platform } | ConvertTo-Json -Compress))
    $login = Invoke-SmokeHttps '/api/platform/auth/login' -Extra @('-H', 'Content-Type: application/json', '--data-binary', "@$loginPath") | ConvertFrom-Json
    Check (![string]::IsNullOrWhiteSpace($login.token)) 'Platform login proxy failed.'
    $loginHeaders = Invoke-SmokeHttps '/api/platform/auth/login' -Extra @('-D', '-', '-o', '/dev/null', '-H', 'Content-Type: application/json', '--data-binary', "@$loginPath")
    Check ($loginHeaders -match 'no-store') 'Login no-store lost at proxy.'
    $tenant = @{
        requestId = [Guid]::NewGuid().ToString(); company = @{ name = 'SYNTHETIC OPS SMOKE'; ruc = '1799999999001'; timeZoneId = 'America/Guayaquil' }
        initialEstablishment = @{ name = 'SMOKE'; address = 'Synthetic only' }; initialEmissionPoint = @{ name = 'SMOKE' }
        initialAdmin = @{ username = 'smoke-tenant'; email = 'smoke-tenant@example.invalid'; password = 'Synthetic-tenant-only-520' }
    }
    $tenantPath = Join-Path $runtime 'tenant.json'
    [IO.File]::WriteAllText($tenantPath, ($tenant | ConvertTo-Json -Depth 5 -Compress))
    $provision = Invoke-SmokeHttps '/api/platform/tenants' -Extra @('-H', 'Content-Type: application/json', '-H', "Authorization: Bearer $($login.token)", '--data-binary', "@$tenantPath") | ConvertFrom-Json
    Check ($provision.tenant.company.name -eq 'SYNTHETIC OPS SMOKE') 'Synthetic provisioning failed.'
    Write-Host 'API PROXY PASS'

    # Only the test-host worker owns fiscal processing during the coherent fake scenario.
    $null = Compose -Arguments @('stop', 'web', 'backend')
    $proof = Compose -Arguments @('run', '--rm', '--no-deps', 'pilot-proof', '--initialize')
    Check ($proof.Contains('PILOT INVOICE/BACKGROUND FAKE/PDF/PURCHASE/STOCK/TRANSFER/NC/REFUND/CLOSE/SETTLEMENT/PROTECTED VALUE PASS')) 'Integrated synthetic pilot proof missing.'
    Write-Host 'INTEGRATED SYNTHETIC PILOT PASS (browser print remains human validation)'

    $null = Compose -Arguments @('exec', '-T', 'postgres', 'sh', '/ops/postgres-backup.sh', '/backups')
    $backup = Compose -Arguments @('exec', '-T', 'postgres', 'sh', '-c', 'ls /backups/*.dump')
    $backup = $backup.Trim()
    $null = Compose -Arguments @('exec', '-T', 'postgres', 'sh', '/ops/verify-postgres-backup.sh', $backup)
    Write-Host 'BACKUP VERIFY PASS'
    $null = Compose -Arguments @('exec', '-T', 'postgres', 'createdb', '-U', 'hfpos_smoke', 'hfpos_ops_restore')
    $null = Compose -Arguments @('exec', '-T', '-e', 'PGDATABASE=hfpos_ops_restore', 'postgres', 'sh', '/ops/postgres-restore.sh', $backup) -ExpectFailure
    $null = Compose -Arguments @('exec', '-T', '-e', 'PGDATABASE=hfpos_ops_restore', '-e', 'HFPOS_RESTORE_APPROVED=YES', 'postgres', 'sh', '/ops/postgres-restore.sh', $backup)
    $null = Compose -Arguments @('exec', '-T', '-e', 'HFPOS_RESTORE_APPROVED=YES', 'postgres', 'sh', '/ops/postgres-restore.sh', $backup) -ExpectFailure
    $null = Compose -Arguments @('exec', '-T', 'postgres', 'createdb', '-U', 'hfpos_smoke', 'hfpos_ops_nonempty')
    $null = Compose -Arguments @('exec', '-T', 'postgres', 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-d', 'hfpos_ops_nonempty', '-c', 'CREATE SCHEMA pgtenant; CREATE TABLE pgtenant.existing(id integer);')
    $null = Compose -Arguments @('exec', '-T', '-e', 'PGDATABASE=hfpos_ops_nonempty', '-e', 'HFPOS_RESTORE_APPROVED=YES', 'postgres', 'sh', '/ops/postgres-restore.sh', $backup) -ExpectFailure
    $null = Compose -Arguments @('exec', '-T', 'postgres', 'sh', '-c', 'cp "$1" /backups/bad.dump; cp "$1.manifest" /backups/bad.dump.manifest; hash=$(sed -n "s/^sha256=//p" "$1.manifest"); printf "%s  bad.dump\n" "$hash" > /backups/bad.dump.sha256; printf corrupted >> /backups/bad.dump', 'sh', $backup)
    $null = Compose -Arguments @('exec', '-T', 'postgres', 'sh', '/ops/verify-postgres-backup.sh', '/backups/bad.dump') -ExpectFailure
    # Safe boolean/count evidence only; never print business rows.
    $query = 'SELECT (SELECT count(*) FROM "Companies" WHERE "Name"=''SYNTHETIC OPS SMOKE'') = 1 AND (SELECT count(*) FROM "Users" WHERE "Username"=''smoke-tenant'') = 1 AND (SELECT count(*) FROM "PlatformUsers" WHERE "Username"=''smoke-platform'') = 1;'
    foreach ($database in @('hfpos_ops_ci', 'hfpos_ops_restore')) {
        $verified = Compose -Arguments @('exec', '-T', 'postgres', 'psql', '-X', '-At', '-U', 'hfpos_smoke', '-d', $database, '-c', $query)
        Check ($verified.Trim() -eq 't') 'Synthetic record verification failed.'
    }
    Write-Host 'RESTORE PASS'
    # The keyring now protects a synthetic SMTP value and synthetic signing fixture from the integrated flow.
    $null = Compose -Arguments @('exec', '-T', 'postgres', 'sh', '/ops/keyring-backup.sh', '/keys', '/backups')
    $archive = (Compose -Arguments @('exec', '-T', 'postgres', 'sh', '-c', 'ls /backups/keyring-*.tar')).Trim()
    $null = Compose -Arguments @('exec', '-T', 'postgres', 'sh', '/ops/keyring-restore.sh', $archive, '/backups/restored-keys') -ExpectFailure
    $null = Compose -Arguments @('exec', '-T', '-e', 'HFPOS_RESTORE_APPROVED=YES', 'postgres', 'sh', '/ops/keyring-restore.sh', $archive, '/backups/restored-keys')
    $null = Compose -Arguments @('exec', '-T', '-e', 'HFPOS_RESTORE_APPROVED=YES', 'postgres', 'sh', '/ops/keyring-restore.sh', $archive, '/backups/restored-keys') -ExpectFailure
    $null = Compose -Arguments @('exec', '-T', 'postgres', 'diff', '-r', '/keys', '/backups/restored-keys')
    $null = Compose -Arguments @('exec', '-T', 'postgres', 'sh', '-c', 'cp "$1" /backups/bad-keyring.tar; hash=$(sha256sum "$1" | cut -d " " -f 1); printf "%s  bad-keyring.tar\n" "$hash" > /backups/bad-keyring.tar.sha256; printf corrupted >> /backups/bad-keyring.tar', 'sh', $archive)
    $null = Compose -Arguments @('exec', '-T', '-e', 'HFPOS_RESTORE_APPROVED=YES', 'postgres', 'sh', '/ops/keyring-restore.sh', '/backups/bad-keyring.tar', '/backups/bad-keys-target') -ExpectFailure
    Write-Host 'KEYRING PASS'
    # Restore ownership only inside this project's ephemeral backup volume, never on source keys.
    $null = Compose -Arguments @('exec', '-T', 'postgres', 'sh', '-c', 'chown -R 1654:1654 /backups/restored-keys; chmod 755 /backups; mkdir /backups/missing-keys /backups/wrong-keys; chown 1654:1654 /backups/missing-keys /backups/wrong-keys')
    $sourceStateBefore = SourceFingerprint
    Check ((BusinessFingerprint -Database 'hfpos_ops_ci') -eq (BusinessFingerprint -Database 'hfpos_ops_restore')) 'Restored data, snapshots, XML or logical sequence state differ from source backup.'
    Invoke-SmokeSequenceNegatives -Backup $backup
    $null = Compose -Arguments @('up', '-d', '--wait', '--wait-timeout', '120', 'restored-backend', 'restored-web')
    Wait-SmokeWebReady -Probe { param($seconds) (Invoke-SmokeHttps '/' -Port 8445 -Extra @('--max-time', "$seconds")) -match '<app-root' }
    $restoredLogin = Invoke-SmokeHttps '/api/platform/auth/login' -Port 8445 -Extra @('-H', 'Content-Type: application/json', '--data-binary', "@$loginPath") | ConvertFrom-Json
    Check (![string]::IsNullOrWhiteSpace($restoredLogin.token)) 'Restored container platform login failed.'
    [IO.File]::WriteAllText($loginPath, (@{ username = 'pilot-tenant'; password = 'Synthetic-only-531-tenant' } | ConvertTo-Json -Compress))
    $restoredTenant = Invoke-SmokeHttps '/api/Auth/login' -Port 8445 -Extra @('-H', 'Content-Type: application/json', '--data-binary', "@$loginPath") | ConvertFrom-Json
    Check (![string]::IsNullOrWhiteSpace($restoredTenant.token)) 'Restored container tenant login failed.'
    $restoredProof = Compose -Arguments @('run', '--rm', '--no-deps', 'restored-proof', '--verify-restored')
    Check ($restoredProof.Contains('RESTORED APP LOGIN/PROTECTED VALUE/BUSINESS PASS')) 'Restored app assurance missing.'
    foreach ($keyPath in @('/recovery/wrong-keys', '/recovery/missing-keys')) {
        $extra = if ($keyPath -eq '/recovery/wrong-keys') { @('--wrong-key') } else { @() }
        $negative = Compose -Arguments (@('run', '--rm', '--no-deps', '-e', "HF_POS_RELEASE_KEYS=$keyPath", 'restored-proof', '--verify-restored', '--expect-key-failure') + $extra)
        Check ($negative.Contains('RESTORED APP WRONG/MISSING KEY FAIL-CLOSED PASS')) 'Wrong/missing key did not fail closed.'
    }
    $applicationNegative = Compose -Arguments @('run', '--rm', '--no-deps', 'restored-proof', '--verify-restored', '--expect-key-failure', '--wrong-application')
    Check ($applicationNegative.Contains('RESTORED APP WRONG/MISSING KEY FAIL-CLOSED PASS')) 'Wrong ApplicationName did not fail closed.'
    $sourceStateAfter = SourceFingerprint
    Check ($sourceStateBefore -eq $sourceStateAfter) 'Recovery changed source data.'
    Write-Host 'TRUE RECOVERY NEW DB/RESTORED KEYRING/LOGIN/DECRYPT/BUSINESS/FAIL-CLOSED/SOURCE INTACT PASS'
    $null = Compose -Arguments @('up', '-d', '--wait', '--wait-timeout', '120', 'backend', 'web')
    $migrationCountBefore = (Compose -Arguments @('exec', '-T', 'postgres', 'psql', '-X', '-At', '-c', 'SELECT count(*) FROM "__EFMigrationsHistory";')).Trim()
    $null = Compose -Arguments @('restart', 'backend')
    $null = Compose -Arguments @('up', '-d', '--wait', '--wait-timeout', '120', 'backend')
    $migrationCountAfter = (Compose -Arguments @('exec', '-T', 'postgres', 'psql', '-X', '-At', '-c', 'SELECT count(*) FROM "__EFMigrationsHistory";')).Trim()
    Check ($migrationCountBefore -eq $migrationCountAfter) 'Backend changed migration state.'
    Check ((Invoke-SmokeHttps '/health/ready' | ConvertFrom-Json).status -eq 'Healthy') 'Restart readiness failed.'
    $null = Compose -Arguments @('exec', '-T', 'postgres', 'diff', '-r', '/keys', '/backups/restored-keys')
    $logs = Compose -Arguments @('logs', '--no-color', 'backend', 'web', 'restored-backend', 'restored-web')
    foreach ($secret in @($pg, $jwt, $platform, $login.token, $restoredLogin.token, $restoredTenant.token, 'Synthetic-only-531-tenant', 'Synthetic-only-531-protected-value', 'Synthetic-tenant-only-520', 'SMOKE-QUERY-MARKER')) {
        Check (!$logs.Contains($secret)) 'Secret detected in container logs.'
    }
    Write-Host "REHEARSAL PASS duration_seconds=$([math]::Round($watch.Elapsed.TotalSeconds, 1))"
    $success = $true
    if (!$Cleanup) {
        Write-Host 'SMOKE ONLY / NEVER PRODUCTION: https://localhost:8444/platform/login'
        Write-Host 'Username: smoke-platform'
        Write-Host 'Synthetic login password is in ignored smoke-runtime/smoke.env (SMOKE_PLATFORM_PASSWORD).'
        Write-Host 'Stop from PowerShell: & .\scripts\ops\stop-deployment-smoke.ps1'
    }
} catch {
    $smokeError = $_
    throw
} finally {
    if ($Cleanup -or !$success) {
        Invoke-SmokeFinalCleanup -CleanupAction { & (Join-Path $PSScriptRoot 'stop-deployment-smoke.ps1') } -SmokeFailed ($null -ne $smokeError)
    }
}
