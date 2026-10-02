param([switch]$Cleanup, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$windowsPlatform = $IsWindows -or $env:OS -eq 'Windows_NT'
$curlExecutable = (Get-Command -Name $(if ($windowsPlatform) { 'curl.exe' } else { 'curl' }) -CommandType Application | Select-Object -First 1).Source
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$runtime = Join-Path $root 'deploy/compose/smoke-runtime'
$compose = Join-Path $root 'deploy/compose/compose.smoke.yml'
$envFile = Join-Path $runtime 'smoke.env'
if (Test-Path -LiteralPath $runtime) { throw 'Smoke runtime already exists. Run stop-deployment-smoke.ps1 first.' }
$null = New-Item -ItemType Directory -Path (Join-Path $runtime 'tls') -Force
$watch = [Diagnostics.Stopwatch]::StartNew()
$success = $false

function DockerCommand([string[]]$Arguments, [switch]$ExpectFailure) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $result = & docker @Arguments 2>&1; $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $previous }
    if ($ExpectFailure) {
        if ($code -eq 0) { throw 'Expected safety refusal did not occur.' }
    } elseif ($code -ne 0) {
        $safeMessage = if (($result -join "`n") -match '(Migration (listing )?failed[^\r\n]*)') { $Matches[1] } else { 'No secret-bearing output emitted.' }
        throw "Docker operation failed: $($Arguments[0]). $safeMessage"
    }
    return ($result -join "`n")
}
function Compose([string[]]$Arguments, [switch]$ExpectFailure) {
    try { DockerCommand -Arguments (@('compose', '--project-name', 'hfpos-520-smoke', '--env-file', $envFile, '-f', $compose) + $Arguments) -ExpectFailure:$ExpectFailure }
    catch { throw "Smoke compose step failed: $($Arguments[0]); $($_.Exception.Message)" }
}
function Check([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Curl([string]$Path, [string[]]$Extra = @()) {
    if ($windowsPlatform) { $Extra = @($Extra | ForEach-Object { if ($_ -eq '/dev/null') { 'NUL' } else { $_ } }) }
    $result = & $curlExecutable -ksS --max-time 15 @Extra "https://localhost:8444$Path"
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic HTTPS probe failed.' }
    return ($result -join "`n")
}

try {
    # Generated credentials are TEST/SMOKE ONLY, NEVER PRODUCTION.
    $pg = [Guid]::NewGuid().ToString('N')
    $jwt = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    $platform = 'Smoke-only-' + [Guid]::NewGuid().ToString('N')
    [IO.File]::WriteAllText($envFile, "SMOKE_PG_PASSWORD=$pg`nSMOKE_JWT_KEY=$jwt`nSMOKE_PLATFORM_PASSWORD=$platform`n", [Text.UTF8Encoding]::new($false))
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
        $null = Compose -Arguments @('--profile', 'migration', 'build', 'backend', 'web', 'migrations')
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
        $image = if ($service -eq 'backend') { 'hfpos/backend:smoke' } elseif ($service -eq 'web') { 'hfpos/web:smoke' } else { 'hfpos/migrations:smoke' }
        $user = DockerCommand -Arguments @('image', 'inspect', '--format', '{{.Config.User}}', $image)
        Check ($user -ne '' -and $user -notmatch '^(root|0)(:|$)') 'Image must be non-root.'
    }
    $backendId = Compose -Arguments @('ps', '-q', 'backend')
    $ports = DockerCommand -Arguments @('inspect', '--format', '{{json .HostConfig.PortBindings}}', $backendId.Trim())
    Check ($ports -in @('{}', 'null')) 'Backend must not publish host ports.'
    Check ((Curl '/') -match '<app-root') 'Angular bundle not served.'
    Check ((Curl '/platform/tenants') -match '<app-root') 'SPA deep link failed.'
    Check ((Curl '/missing-asset.js' -Extra @('-o', '/dev/null', '-w', '%{http_code}')) -eq '404') 'Missing asset must return 404.'
    Check ((Curl '/api/missing' -Extra @('-o', '/dev/null', '-w', '%{http_code}')) -eq '404') 'API must not fall back to Angular.'
    $indexHeaders = Curl '/index.html' -Extra @('-I')
    Check ($indexHeaders -match 'no-cache, must-revalidate') 'Index caching contract failed.'
    $asset = [regex]::Match((Curl '/'), 'src="([^\"]+-[A-Za-z0-9_-]{8,}\.js)"').Groups[1].Value
    Check (![string]::IsNullOrEmpty($asset)) 'Hashed JavaScript asset missing.'
    Check ((Curl "/$asset" -Extra @('-I')) -match 'immutable') 'Hashed asset cache failed.'
    Check (((Curl '/health/live' -Extra @('-H', 'X-Forwarded-Proto: http') | ConvertFrom-Json).status) -eq 'Healthy') 'Proxy must replace forwarded proto.'
    # Synthetic bytes only, NOT a certificate. Verify edge does not reject backend-admissible sizes.
    $body = Join-Path $runtime 'large-body.bin'
    [IO.File]::WriteAllBytes($body, [byte[]]::new(2 * 1024 * 1024))
    Check ((Curl '/api/missing' -Extra @('-X', 'POST', '--data-binary', "@$body", '-o', '/dev/null', '-w', '%{http_code}')) -ne '413') 'Proxy reduced the existing multipart size contract.'
    [IO.File]::WriteAllBytes($body, [byte[]]::new(4 * 1024 * 1024))
    Check ((Curl '/api/missing?password=SMOKE-QUERY-MARKER' -Extra @('-X', 'POST', '--data-binary', "@$body", '-o', '/dev/null', '-w', '%{http_code}')) -eq '413') 'Oversized edge request must be refused.'
    $redirect = & $curlExecutable -sS -I --max-time 10 'http://localhost:8084/'
    Check (($redirect -join "`n") -match '308' -and ($redirect -join "`n") -match 'https://localhost:8444') 'HTTP edge redirect failed.'
    foreach ($path in @('/health/live', '/health/ready')) {
        $health = Curl $path | ConvertFrom-Json
        Check ($health.status -eq 'Healthy') 'Health check failed.'
    }
    Write-Host 'WEB PASS'
    $loginPath = Join-Path $runtime 'login.json'
    [IO.File]::WriteAllText($loginPath, (@{ username = 'smoke-platform'; password = $platform } | ConvertTo-Json -Compress))
    $login = Curl '/api/platform/auth/login' -Extra @('-H', 'Content-Type: application/json', '--data-binary', "@$loginPath") | ConvertFrom-Json
    Check (![string]::IsNullOrWhiteSpace($login.token)) 'Platform login proxy failed.'
    $loginHeaders = Curl '/api/platform/auth/login' -Extra @('-D', '-', '-o', '/dev/null', '-H', 'Content-Type: application/json', '--data-binary', "@$loginPath")
    Check ($loginHeaders -match 'no-store') 'Login no-store lost at proxy.'
    $tenant = @{
        requestId = [Guid]::NewGuid().ToString(); company = @{ name = 'SYNTHETIC OPS SMOKE'; ruc = '1799999999001'; timeZoneId = 'America/Guayaquil' }
        initialEstablishment = @{ name = 'SMOKE'; address = 'Synthetic only' }; initialEmissionPoint = @{ name = 'SMOKE' }
        initialAdmin = @{ username = 'smoke-tenant'; email = 'smoke-tenant@example.invalid'; password = 'Synthetic-tenant-only-520' }
    }
    $tenantPath = Join-Path $runtime 'tenant.json'
    [IO.File]::WriteAllText($tenantPath, ($tenant | ConvertTo-Json -Depth 5 -Compress))
    $provision = Curl '/api/platform/tenants' -Extra @('-H', 'Content-Type: application/json', '-H', "Authorization: Bearer $($login.token)", '--data-binary', "@$tenantPath") | ConvertFrom-Json
    Check ($provision.tenant.company.name -eq 'SYNTHETIC OPS SMOKE') 'Synthetic provisioning failed.'
    Write-Host 'API PROXY PASS'

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
    # Synthetic flat-file fixture verifies the mounted keyring backup, without real keys/secrets.
    $null = Compose -Arguments @('exec', '-T', 'backend', 'bash', '-c', 'test -w /keys && printf "synthetic-smoke-only" > /keys/smoke-fixture.txt')
    $null = Compose -Arguments @('exec', '-T', 'postgres', 'sh', '/ops/keyring-backup.sh', '/keys', '/backups')
    $archive = (Compose -Arguments @('exec', '-T', 'postgres', 'sh', '-c', 'ls /backups/keyring-*.tar')).Trim()
    $null = Compose -Arguments @('exec', '-T', 'postgres', 'sh', '/ops/keyring-restore.sh', $archive, '/backups/restored-keys') -ExpectFailure
    $null = Compose -Arguments @('exec', '-T', '-e', 'HFPOS_RESTORE_APPROVED=YES', 'postgres', 'sh', '/ops/keyring-restore.sh', $archive, '/backups/restored-keys')
    $null = Compose -Arguments @('exec', '-T', '-e', 'HFPOS_RESTORE_APPROVED=YES', 'postgres', 'sh', '/ops/keyring-restore.sh', $archive, '/backups/restored-keys') -ExpectFailure
    $null = Compose -Arguments @('exec', '-T', 'postgres', 'diff', '-r', '/keys', '/backups/restored-keys')
    $null = Compose -Arguments @('exec', '-T', 'postgres', 'sh', '-c', 'cp "$1" /backups/bad-keyring.tar; hash=$(sha256sum "$1" | cut -d " " -f 1); printf "%s  bad-keyring.tar\n" "$hash" > /backups/bad-keyring.tar.sha256; printf corrupted >> /backups/bad-keyring.tar', 'sh', $archive)
    $null = Compose -Arguments @('exec', '-T', '-e', 'HFPOS_RESTORE_APPROVED=YES', 'postgres', 'sh', '/ops/keyring-restore.sh', '/backups/bad-keyring.tar', '/backups/bad-keys-target') -ExpectFailure
    Write-Host 'KEYRING PASS'
    $migrationCountBefore = (Compose -Arguments @('exec', '-T', 'postgres', 'psql', '-X', '-At', '-c', 'SELECT count(*) FROM "__EFMigrationsHistory";')).Trim()
    $null = Compose -Arguments @('restart', 'backend')
    $null = Compose -Arguments @('up', '-d', '--wait', '--wait-timeout', '120', 'backend')
    $migrationCountAfter = (Compose -Arguments @('exec', '-T', 'postgres', 'psql', '-X', '-At', '-c', 'SELECT count(*) FROM "__EFMigrationsHistory";')).Trim()
    Check ($migrationCountBefore -eq $migrationCountAfter) 'Backend changed migration state.'
    Check ((Curl '/health/ready' | ConvertFrom-Json).status -eq 'Healthy') 'Restart readiness failed.'
    $null = Compose -Arguments @('exec', '-T', 'postgres', 'diff', '-r', '/keys', '/backups/restored-keys')
    $logs = Compose -Arguments @('logs', '--no-color', 'backend', 'web')
    foreach ($secret in @($pg, $jwt, $platform, $login.token, 'Synthetic-tenant-only-520', 'SMOKE-QUERY-MARKER')) {
        Check (!$logs.Contains($secret)) 'Secret detected in container logs.'
    }
    Write-Host "REHEARSAL PASS duration_seconds=$([math]::Round($watch.Elapsed.TotalSeconds, 1))"
    $success = $true
    if (!$Cleanup) {
        Write-Host 'SMOKE ONLY / NEVER PRODUCTION: https://localhost:8444/platform/login'
        Write-Host 'Username: smoke-platform'
        Write-Host 'Synthetic login password is in ignored smoke-runtime/smoke.env (SMOKE_PLATFORM_PASSWORD).'
        Write-Host 'Stop: pwsh -File scripts/ops/stop-deployment-smoke.ps1'
    }
} finally {
    if ($Cleanup -or !$success) { & (Join-Path $PSScriptRoot 'stop-deployment-smoke.ps1') }
}
