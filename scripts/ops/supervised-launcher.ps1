param([switch]$Setup, [string]$AgentSid)
$ErrorActionPreference = 'Stop'
function New-OwnedCopy([string]$Source, [string]$Target, [string]$Helper) {
    & $Helper -LiteralPath $Target -CreateFile
    if ($LASTEXITCODE -ne 0) { throw 'Private resource creation failed.' }
    [IO.File]::WriteAllBytes($Target, [IO.File]::ReadAllBytes($Source))
}
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $destination = Join-Path $env:LOCALAPPDATA 'HfposSupervised'
    if ($Setup) {
        if (!$AgentSid -or $AgentSid -eq $identity.User.Value) { throw 'Separate human principal required.' }
        if (Test-Path -LiteralPath $destination) { throw 'Existing installation: manual reviewed upgrade required.' }
        $sha = Read-Host 'SHA main integrado revisado (40 hex)'
        if ($sha -cnotmatch '^[a-f0-9]{40}$') { throw 'Invalid SHA.' }
        $comparison = & gh api "repos/fhamann79/hfpos/compare/${sha}...main" | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or $comparison.merge_base_commit.sha -ne $sha -or $comparison.status -notin @('ahead','identical')) { throw 'Not integrated main.' }
        $hash = & python (Join-Path $PSScriptRoot 'supervised-operations.py') digest
        if ($LASTEXITCODE -ne 0 -or $hash -cnotmatch '^[a-f0-9]{64}$') { throw 'Invalid bundle.' }
        $expected = Read-Host 'Bundle SHA256 revisado publicado por workflow main'
        if ($hash -cne $expected) { throw 'Bundle mismatch.' }
        $null = New-Item -ItemType Directory -Path $destination
        $acl = New-Object Security.AccessControl.DirectorySecurity
        $acl.SetAccessRuleProtection($true, $false)
        $acl.SetOwner($identity.User)
        foreach ($sid in @($identity.User, (New-Object Security.Principal.SecurityIdentifier('S-1-5-18')))) {
            $rule = New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow')
            $acl.SetAccessRule($rule)
        }
        Set-Acl -LiteralPath $destination -AclObject $acl
        $names = @('supervised-operations.py','supervised-archive.py','supervised-bridge.py','supervised-local-acl.ps1',
                   'supervised-launcher.ps1','supervised-ssh.sh','supervised-root.sh','postgres-restore.sh',
                   'verify-postgres-backup.sh','verify-restored-db.sh','keyring-restore.sh')
        $helper = Join-Path $PSScriptRoot 'supervised-local-acl.ps1'
        foreach ($name in $names) { New-OwnedCopy (Join-Path $PSScriptRoot $name) (Join-Path $destination $name) $helper }
        $config = [ordered]@{version=1;source_sha=$sha;bundle_sha256=$hash;agent_sid=$AgentSid;human_sid=$identity.User.Value}
        $config.host = Read-Host 'Host SSH aprobado (sin abrir rangos GHA)'
        $config.port = [int](Read-Host 'Puerto SSH aprobado')
        $config.transport_key = Read-Host 'Ruta privada clave SSH transporte dedicada'
        $config.signing_key = Read-Host 'Ruta privada clave firma humana separada (FIDO opcional)'
        $config.known_hosts = Read-Host 'Ruta known_hosts fijado y verificado fuera de banda'
        $config.age_identity = Read-Host 'Ruta privada age EXTERNA (no se copia al host)'
        $config.ciphertext_directory = Read-Host 'Carpeta privada de ciphertext opaco ID.age'
        & $helper -LiteralPath (Join-Path $destination 'bridge.json') -CreateFile
        if ($LASTEXITCODE -ne 0) { throw 'Private config creation failed.' }
        $config | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $destination 'bridge.json') -Encoding UTF8
        & python (Join-Path $destination 'supervised-bridge.py') --check-installation
        if ($LASTEXITCODE -ne 0) { throw 'Human custody/installation check failed.' }
        $hostPackage = Join-Path $destination 'host-bootstrap'
        & $helper -LiteralPath $hostPackage -CreateDirectory
        if ($LASTEXITCODE -ne 0) { throw 'Private package creation failed.' }
        foreach ($name in $names) { New-OwnedCopy (Join-Path $destination $name) (Join-Path $hostPackage $name) $helper }
        New-OwnedCopy ($config.signing_key + '.pub') (Join-Path $hostPackage 'human-signing.pub') $helper
        New-OwnedCopy ($config.transport_key + '.pub') (Join-Path $hostPackage 'transport.pub') $helper
        $entry = "#!/bin/sh`nset -eu`ncd -- `"`$(dirname -- `"`$0`")`"`nexec /usr/bin/python3 supervised-operations.py bootstrap --source-sha $sha --bundle-sha256 $hash --signer-public human-signing.pub --transport-public transport.pub`n"
        & $helper -LiteralPath (Join-Path $hostPackage 'bootstrap.sh') -CreateFile
        if ($LASTEXITCODE -ne 0) { throw 'Private entry creation failed.' }
        [IO.File]::WriteAllText((Join-Path $hostPackage 'bootstrap.sh'), $entry, (New-Object Text.UTF8Encoding($false)))
        Write-Host 'Preparacion lista. El paquete host-bootstrap contiene SOLO codigo revisado y claves publicas.'
        Write-Host 'Instalacion Ubuntu humana: copiar ese paquete verificado por canal admin existente y ejecutar sudo sh bootstrap.sh UNA vez.'
        Write-Host 'Conectividad/custodia efectiva siguen pendientes; el asistente no instala ni arranca el puente.'
        exit 0
    }
    & python (Join-Path $destination 'supervised-bridge.py') --foreground
    exit $LASTEXITCODE
} catch {
    Write-Host 'BOOTSTRAP/LAUNCH DENIED: requiere instalacion humana revisada y principal separado.'
    exit 1
}
