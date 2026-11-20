param([string]$Destino,[string]$OrigenIPv4,[string]$OrigenIPv6,[switch]$Reconciliar)
$ErrorActionPreference='Stop'
if (!$Destino) { $Destino=Read-Host 'Destino SSH habitual ya verificado' }
if ($Destino -cnotmatch '^(hfadmin@)?[A-Za-z0-9][A-Za-z0-9.-]{0,252}$') { throw 'NO-GO SSH_DESTINO_NO_VALIDO' }
$perfil = & ssh.exe -G -- $Destino 2>$null
if ($LASTEXITCODE -ne 0 -or ($perfil -match '^(localforward|remoteforward|dynamicforward) ')) {
    throw 'NO-GO PERFIL_SSH_CON_FORWARD_ADICIONAL_O_NO_VERIFICADO'
}
$root='C:\Users\fhama\source\repos\hfpos'
$payload=& python.exe -B (Join-Path $PSScriptRoot 'Empaquetar-DEV532-RC11.py') $root
if ($LASTEXITCODE -ne 0 -or @($payload).Count -ne 1 -or $payload -cnotmatch '^[A-Za-z0-9+/=]{1,28000}$') { throw 'NO-GO PAQUETE_LOCAL' }
$baseCommand='set -- ${SSH_CONNECTION-}; exec sudo -- python3 -I -B -c ''import sys,base64,lzma;exec(lzma.decompress(base64.b64decode(sys.argv[1])))'' ' + $payload
if ($Reconciliar) {
    & ssh.exe -tt -o StrictHostKeyChecking=yes -o ClearAllForwardings=yes -o ForwardAgent=no -- $Destino ($baseCommand+' reconcile $4')
    return
}
if (!$OrigenIPv4) { $OrigenIPv4=Read-Host 'IPv4 de origen del VPS (inventario privado; no IP de CDN)' }
if (!$OrigenIPv6) { $OrigenIPv6=Read-Host 'IPv6 de origen del VPS (inventario privado; no IP de CDN)' }
$v4=[Net.IPAddress]::Parse($OrigenIPv4); $v6=[Net.IPAddress]::Parse($OrigenIPv6)
if ($v4.AddressFamily -ne 'InterNetwork' -or $v6.AddressFamily -ne 'InterNetworkV6' -or
    $v6.IsIPv4MappedToIPv6 -or $v6.IsIPv6LinkLocal -or $v6.ScopeId -ne 0 -or
    [Net.IPAddress]::IsLoopback($v4) -or [Net.IPAddress]::IsLoopback($v6)) { throw 'NO-GO ORIGEN_DUALSTACK_NO_VALIDO' }
$observer=Join-Path $PSScriptRoot 'Sondeo-DEV532-RC11.ps1'
$exe=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$window=Start-Process -FilePath $exe -WindowStyle Normal -PassThru -ArgumentList @('-NoProfile','-File',('"' + $observer + '"'),'-Ip4',$v4.ToString(),'-Ip6',$v6.ToString(),'-Destino',$Destino)
try {
    Write-Host 'ANTES: confirma que el monitor muestra TLS4/TLS6=True, SSH 4/6 y AUTH NUEVA=True.'
    Write-Host 'CIERRE: exige dos muestras TCP4/TCP6=False y publica=False, con SSH 4/6/AUTH NUEVA=True y TLS por tunel=True.'
    Write-Host 'APERTURA: verifica TLS de origen y publica; si falla, no repitas ni reviertas automaticamente.'
    $remote=$baseCommand+' cutover $4'
    & ssh.exe -tt -o StrictHostKeyChecking=yes -o ControlMaster=no -o ControlPath=none -o ForwardAgent=no -o ForwardX11=no -o GatewayPorts=no -o ExitOnForwardFailure=yes -L '127.0.0.1:7443:127.0.0.1:443' -- $Destino $remote
    $code=$LASTEXITCODE
    if ($code -ne 0) { Write-Host 'STOP. Si el VPS quedo en contencion, consulta el runbook antes de cualquier retorno.' }
    else { Write-Host 'No registres ventas de prueba. Comprueba el monitor antes de cerrar.' }
    $null=Read-Host 'Enter para cerrar el monitor local'
} finally {
    if (!$window.HasExited) { $window.Kill() }
    $window.Dispose()
}
