# Windows PowerShell 5.1: verify the exact launcher wiring without opening any socket.
$ErrorActionPreference = 'Stop'
$global:Rc11FixtureModes = @()
$global:Rc11FixtureWindows = 0

function python.exe {
    $global:LASTEXITCODE = 0
    'UkMxMS1TRU5USU5FTA=='
}

function Start-Process {
    $global:Rc11FixtureWindows++
    $fixture = [pscustomobject]@{ HasExited = $true }
    $fixture | Add-Member ScriptMethod Dispose {}
    $fixture
}

function Read-Host { '' }

function ssh.exe {
    $global:LASTEXITCODE = 0
    if ($args[0] -eq '-G') { 'hostname fixture.invalid'; return }
    $mode = [regex]::Match($args[-1], ' UkMxMS1TRU5USU5FTA== (?:(cutover|verify-network) \$4 192\.0\.2\.2 2001:db8::2|(reconcile) \$4)$')
    if (!$mode.Success -or $args -notcontains 'StrictHostKeyChecking=yes') { throw 'Unexpected SSH command' }
    $action = $mode.Groups[1].Value + $mode.Groups[2].Value
    $global:Rc11FixtureModes += $action
    $hasTunnel = $args -contains '127.0.0.1:7443:127.0.0.1:443'
    if ($hasTunnel -ne ($action -eq 'cutover')) { throw 'Unexpected SSH tunnel' }
}

try {
    $launcher = Join-Path $PSScriptRoot 'Ejecutar-DEV532-RC11.ps1'
    & $launcher -Destino 'hfadmin@fixture.invalid' -OrigenIPv4 '192.0.2.2' -OrigenIPv6 '2001:db8::2' 6>$null
    & $launcher -Destino 'hfadmin@fixture.invalid' -Reconciliar 6>$null
    & $launcher -Destino 'hfadmin@fixture.invalid' -OrigenIPv4 '192.0.2.2' -OrigenIPv6 '2001:db8::2' -VerificarBridge 6>$null
    if (($global:Rc11FixtureModes -join ',') -ne 'cutover,reconcile,verify-network' -or $global:Rc11FixtureWindows -ne 1) {
        throw 'Unexpected execution sequence'
    }
    Write-Output 'PASS RC11 Windows PowerShell launcher with mocked SSH'
}
finally {
    Remove-Variable Rc11FixtureModes,Rc11FixtureWindows -Scope Global
}
