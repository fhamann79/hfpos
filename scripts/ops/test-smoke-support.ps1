param([switch]$NativeFixture, [int]$FixtureExitCode = 0, [string]$FixtureArgument)
if ($NativeFixture) {
    [Console]::Error.WriteLine('SYNTHETIC-SECRET-DO-NOT-LOG')
    [Console]::Out.WriteLine($FixtureArgument)
    exit $FixtureExitCode
}

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'smoke-support.ps1')
$nativeImplementation = (Get-Item Function:Invoke-SmokeNativeCommand).ScriptBlock
$processExecutable = (Get-Process -Id $PID).Path
$testScript = $PSCommandPath
$passed = 0
function Assert-Test([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Test-Case([string]$Name, [scriptblock]$Action) {
    & $Action
    $script:passed++
    Write-Host "PASS $Name"
}

Test-Case 'restored proxy overrides the template actually consumed by its entrypoint' {
    $root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $entrypoint = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'web-entrypoint.sh') -Raw
    $compose = Get-Content -LiteralPath (Join-Path $root 'deploy/compose/compose.smoke.yml') -Raw
    Assert-Test ($entrypoint.Contains('< /etc/nginx/default.conf.template')) 'Entrypoint template contract changed.'
    Assert-Test ($compose.Contains('./smoke-runtime/restored.conf.template:/etc/nginx/default.conf.template:ro')) 'Restored proxy still consumes the original upstream template.'
}

Test-Case 'rehearsal identity is isolated from legacy and cleanup matches its owner' {
    $identity = Get-SmokeIdentity -Root $PSScriptRoot
    Assert-Test ($identity.Project -match '^hfpos-dev531-[a-f0-9]{12}$') 'Rehearsal must not target the legacy project.'
    Assert-Test ($identity.Project -eq (Get-SmokeIdentity -Root $PSScriptRoot).Project) 'Cleanup identity must be stable.'
    Assert-Test ($identity.Project -ne (Get-SmokeIdentity -Root ($PSScriptRoot + '-other')).Project) 'Worktrees must not share resources.'
}

Test-Case 'native stderr with exit zero is not a failure' {
    $result = Invoke-SmokeNativeCommand -FilePath $processExecutable -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $testScript, '-NativeFixture', '-FixtureArgument', 'fixture-output')
    Assert-Test ($result.ExitCode -eq 0) 'Native stderr was treated as failure.'
    Assert-Test ($result.Output.Contains('fixture-output')) 'Native stdout lost.'
    Assert-Test ($ErrorActionPreference -eq 'Stop') 'Error preference was not restored.'
}
Test-Case 'native nonzero exit is captured' {
    $result = Invoke-SmokeNativeCommand -FilePath $processExecutable -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $testScript, '-NativeFixture', '-FixtureExitCode', '7')
    Assert-Test ($result.ExitCode -eq 7) 'Native failure exit code lost.'
}
Test-Case 'missing native executable cannot reuse a previous successful exit' {
    $LASTEXITCODE = 0
    $failure = $null
    try { Invoke-SmokeNativeCommand -FilePath 'hfpos-synthetic-missing-executable' -Arguments @() } catch { $failure = $_ }
    Assert-Test ($null -ne $failure -and $failure.Exception.Message -eq 'Smoke native executable is unavailable.') 'Missing executable was mistaken for success.'
}
Test-Case 'SQL and shell embedded quotes reach the native process intact' {
    foreach ($argument in @('SELECT count(*) FROM "Companies";', 'printf "%s\n" "$1"', 'synthetic path with spaces')) {
        $result = Invoke-SmokeNativeCommand -FilePath $processExecutable -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $testScript, '-NativeFixture', '-FixtureArgument', $argument)
        Assert-Test ($result.ExitCode -eq 0 -and $result.Output.Contains($argument)) 'Native argument fidelity failed.'
    }
}

$fixtureRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('hfpos-smoke-tests-' + [Guid]::NewGuid().ToString('N'))))
$runtime = Join-Path $fixtureRoot 'deploy/compose/smoke-runtime'
try {
    # Cleanup tests substitute Docker only; no container or database is touched.
    function Invoke-SmokeNativeCommand {
        param([string]$FilePath, [string[]]$Arguments)
        Assert-Test ($FilePath -eq 'docker' -and $Arguments -contains (Get-SmokeIdentity -Root $fixtureRoot).Project -and $Arguments -contains '--context' -and $Arguments -contains '--profile' -and $Arguments -contains '*' -and $Arguments -contains '--remove-orphans') 'Unexpected cleanup command or incomplete profile cleanup.'
        return [pscustomobject]@{ ExitCode = $script:fixtureCode; Output = 'SYNTHETIC-SECRET-DO-NOT-LOG' }
    }
    Test-Case 'successful cleanup deletes runtime and keeps adjacent files' {
        $null = New-Item -ItemType Directory -Path $runtime -Force
        $null = New-Item -ItemType File -Path (Join-Path $runtime 'smoke.env')
        $adjacent = Join-Path $fixtureRoot 'deploy/compose/keep.txt'
        $null = New-Item -ItemType File -Path $adjacent
        $script:fixtureCode = 0
        $messages = @(Invoke-SmokeResourceCleanup -Root $fixtureRoot 6>&1)
        Assert-Test (!(Test-Path -LiteralPath $runtime) -and (Test-Path -LiteralPath $adjacent)) 'Successful cleanup scope failed.'
        Assert-Test (($messages -join "`n") -match 'SMOKE CLEANUP PASS') 'Cleanup did not report success.'
        Assert-Test (($messages -join "`n") -notmatch 'SYNTHETIC-SECRET') 'Cleanup exposed native output.'
    }
    Test-Case 'failed cleanup preserves runtime and emits a safe error' {
        $null = New-Item -ItemType Directory -Path $runtime -Force
        $null = New-Item -ItemType File -Path (Join-Path $runtime 'smoke.env')
        $script:fixtureCode = 7
        $failure = $null
        try { Invoke-SmokeResourceCleanup -Root $fixtureRoot } catch { $failure = $_ }
        Assert-Test ($null -ne $failure -and (Test-Path -LiteralPath $runtime)) 'Failed cleanup deleted runtime or did not fail.'
        Assert-Test ($failure.Exception.Message -eq 'Smoke resource cleanup failed; runtime preserved for retry.') 'Cleanup failure was not sanitized.'
    }
    Test-Case 'original smoke error survives a secondary cleanup failure' {
        $original = $null
        $warnings = @()
        try {
            $smokeError = $null
            try { throw 'Synthetic original assertion failed.' }
            catch { $smokeError = $_; throw }
            finally {
                $warnings = @(Invoke-SmokeFinalCleanup -CleanupAction { throw 'SYNTHETIC-SECRET-DO-NOT-LOG' } -SmokeFailed ($null -ne $smokeError) 3>&1)
            }
        } catch { $original = $_ }
        Assert-Test ($original.Exception.Message -eq 'Synthetic original assertion failed.') 'Cleanup masked the original error.'
        Assert-Test (($warnings -join "`n") -match 'Secondary error') 'Secondary cleanup failure was not reported.'
        Assert-Test (($warnings -join "`n") -notmatch 'SYNTHETIC-SECRET') 'Secondary warning exposed native output.'
    }
    Test-Case 'cleanup failure after successful smoke still fails safely' {
        $failure = $null
        try { Invoke-SmokeFinalCleanup -CleanupAction { throw 'SYNTHETIC-SECRET-DO-NOT-LOG' } -SmokeFailed $false } catch { $failure = $_ }
        Assert-Test ($null -ne $failure -and $failure.Exception.Message -eq 'Smoke resource cleanup failed; runtime preserved for retry.') 'Cleanup failure was hidden or unsafe.'
    }
    Test-Case 'HTTPS helper is not shadowed by the Windows curl alias' {
        $tokens = $null; $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'start-deployment-smoke.ps1'), [ref]$tokens, [ref]$errors)
        Assert-Test ($errors.Count -eq 0) 'Smoke script has parser errors.'
        $definition = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-SmokeHttps' }, $true)
        . ([scriptblock]::Create($definition.Extent.Text))
        function Invoke-SmokeNativeCommand { param($FilePath, $Arguments) [pscustomobject]@{ ExitCode = 0; Output = '<app-root></app-root>' } }
        if (!(Get-Alias -Name curl -ErrorAction SilentlyContinue)) { Set-Alias -Name curl -Value MustNotRun -Scope Local }
        $curlExecutable = $processExecutable
        $windowsPlatform = $env:OS -eq 'Windows_NT'
        Assert-Test ((Invoke-SmokeHttps '/') -match '<app-root') 'HTTPS helper collided with the curl alias.'
    }
    Test-Case 'web readiness retries transient failures then succeeds' {
        $script:attempts = 0
        Wait-SmokeWebReady -TimeoutSeconds 1 -RetryMilliseconds 10 -Probe {
            param($seconds)
            Assert-Test ($seconds -ge 1 -and $seconds -le 5) 'Probe request timeout is unbounded.'
            $script:attempts++
            if ($script:attempts -eq 1) { throw 'SYNTHETIC-SECRET-DO-NOT-LOG' }
            return $script:attempts -ge 3
        }
        Assert-Test ($script:attempts -eq 3) 'Readiness did not retry correctly.'
    }
    Test-Case 'web readiness expires with a clear safe error' {
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $failure = $null
        try { Wait-SmokeWebReady -TimeoutSeconds 1 -RetryMilliseconds 10 -Probe { throw 'SYNTHETIC-SECRET-DO-NOT-LOG' } } catch { $failure = $_ }
        Assert-Test ($failure.Exception.Message -eq 'Synthetic web readiness timed out after 1 seconds (HTTPS Angular bundle probe).') 'Readiness failure lost its safe diagnosis.'
        Assert-Test ($timer.Elapsed.TotalSeconds -lt 10) 'Readiness retry was not bounded.'
    }
} finally {
    Set-Item Function:Invoke-SmokeNativeCommand -Value $nativeImplementation
    $boundary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (!$fixtureRoot.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup target.' }
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}
Write-Host "SMOKE SUPPORT TESTS PASS count=$passed PowerShell=$($PSVersionTable.PSVersion)"
