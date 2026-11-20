param([string]$Ip4,[string]$Ip6,[string]$Destino)
$ErrorActionPreference='Continue'
$curl=(Get-Command curl.exe -ErrorAction Stop).Source
function Open-PrivateBrowser {
    $edge=Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'
    if (!(Test-Path -LiteralPath $edge -PathType Leaf)) { Write-Host 'NO-GO: Edge local no disponible'; return }
    $profile=Join-Path $env:TEMP ('hf-one-rc11-edge-' + [Guid]::NewGuid().ToString('N'))
    Start-Process -FilePath $edge -ArgumentList @('--inprivate','--no-first-run','--disable-sync',
        '--no-proxy-server','--disable-background-networking','--host-resolver-rules="MAP hfone.app 127.0.0.1"',
        ('--user-data-dir="' + $profile + '"'),'https://hfone.app:7443/login')
}
Write-Host 'Pulsa B aqui, solo tras GO_POSTCORTE, para abrir login privado. Cierra Edge al terminar.'
function Tcp([string]$ip,[int]$port) {
    $addr=[Net.IPAddress]::Parse($ip)
    $c=[Net.Sockets.TcpClient]::new($addr.AddressFamily)
    try {
        $ar=$c.BeginConnect($addr,$port,$null,$null)
        if (!$ar.AsyncWaitHandle.WaitOne(1600)) { return $false }
        $c.EndConnect($ar)
        $true
    } catch { $false } finally { $c.Dispose() }
}
function Tls([string[]]$route) {
    $nonce=[DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    & $curl --disable --noproxy '*' -fsS --max-time 5 -H 'Cache-Control: no-cache' -o NUL @route "https://hfone.app/health/ready?preflight=$nonce" 2>$null
    $LASTEXITCODE -eq 0
}
for (;;) {
    $t4=Tcp $Ip4 443
    $t6=Tcp $Ip6 443
    $h4=$t4 -and (Tls @('-4','--resolve',"hfone.app:443:$Ip4"))
    $h6=$t6 -and (Tls @('-6','--resolve',"hfone.app:443:[$Ip6]"))
    $ss4=Tcp $Ip4 22
    $ss6=Tcp $Ip6 22
    & ssh.exe -n -o BatchMode=yes -o ConnectTimeout=4 -o StrictHostKeyChecking=yes -o ControlMaster=no -o ControlPath=none -o ForwardAgent=no -o ClearAllForwardings=yes -- $Destino true 2>$null | Out-Null
    $fresh=$LASTEXITCODE -eq 0
    $tunnel=Tls @('--connect-to','hfone.app:443:127.0.0.1:7443')
    $front=Tls @()
    Write-Host ('{0:HH:mm:ss}  HTTPS ORIGEN TCP4={1} TCP6={2} TLS4={3} TLS6={4} | SSH 4={5} 6={6} AUTH NUEVA={7} | TLS por tunel={8} publica={9}' -f (Get-Date),$t4,$t6,$h4,$h6,$ss4,$ss6,$fresh,$tunnel,$front)
    if ([Console]::KeyAvailable -and [Console]::ReadKey($true).Key -eq 'B' -and $tunnel -and !$t4 -and !$t6) {
        Open-PrivateBrowser
    }
    Start-Sleep -Seconds 3
}
