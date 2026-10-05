param(
    [switch]$Initialize,
    [switch]$Verify,
    [switch]$VerifyConfiguration,
    [switch]$VerifyListener
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($env:HF_POS_TEST_CONNECTION_STRING)) {
    throw 'Coordinator must supply the isolated hfpos_test_530_smoke loopback connection.'
}
if ($Verify -and -not $Initialize) { throw 'Verification requires explicit disposable initialization.' }
if ($VerifyListener -and ($Initialize -or $Verify -or $VerifyConfiguration)) { throw 'TLS-only verification cannot combine other modes.' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$arguments = @('run', '--project', 'backend/Pos.Backend.Api/Hfpos.FiscalSmokeHost', '--configuration', 'Release', '--no-build', '--', '--assisted-recovery')
if ($Initialize) { $arguments += '--initialize' }
if ($Verify) { $arguments += '--verify' }
if ($VerifyConfiguration) { $arguments += '--verify-configuration' }
if ($VerifyListener) { $arguments += '--verify-listener' }
Push-Location -LiteralPath $root
try {
    # The host checks exact database/user/loopback before opening or initializing any database.
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Assisted recovery host failed ($LASTEXITCODE)." }
} finally { Pop-Location }
