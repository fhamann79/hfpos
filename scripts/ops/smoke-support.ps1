function Invoke-SmokeNativeCommand {
    param([string]$FilePath, [string[]]$Arguments)
    try { $executable = (Get-Command -Name $FilePath -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source }
    catch { throw 'Smoke native executable is unavailable.' }
    # Windows PowerShell's legacy native binding removes embedded SQL/shell quotes.
    if ($env:OS -eq 'Windows_NT' -and $PSVersionTable.PSVersion.Major -le 5) {
        $Arguments = @($Arguments | ForEach-Object { $_.Replace('"', '\"') })
    }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $global:LASTEXITCODE = $null
        $output = & $executable @Arguments 2>&1
        $code = $global:LASTEXITCODE
    } finally { $ErrorActionPreference = $previous }
    if ($null -eq $code) { throw 'Smoke native process did not return an exit code.' }
    return [pscustomobject]@{ ExitCode = $code; Output = ($output -join "`n") }
}

function Invoke-SmokeResourceCleanup {
    param([string]$Root)
    $runtime = [IO.Path]::GetFullPath((Join-Path $Root 'deploy/compose/smoke-runtime'))
    $expected = [IO.Path]::GetFullPath((Join-Path $Root 'deploy/compose')) + [IO.Path]::DirectorySeparatorChar
    if (!$runtime.StartsWith($expected, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe cleanup target.' }
    $envFile = Join-Path $runtime 'smoke.env'
    if (Test-Path -LiteralPath $envFile) {
        $identity = Get-SmokeIdentity -Root $Root
        $result = Invoke-SmokeNativeCommand -FilePath 'docker' -Arguments @('--context', $identity.Context, 'compose', '--project-name', $identity.Project, '--env-file', $envFile, '-f', (Join-Path $Root 'deploy/compose/compose.smoke.yml'), '--profile', '*', 'down', '-v', '--remove-orphans')
        if ($result.ExitCode -ne 0) { throw 'Smoke resource cleanup failed; runtime preserved for retry.' }
        Remove-Item -LiteralPath $runtime -Recurse -Force
        Write-Host 'SMOKE CLEANUP PASS'
    }
}

function Invoke-SmokeFinalCleanup {
    param([scriptblock]$CleanupAction, [bool]$SmokeFailed)
    try { & $CleanupAction }
    catch {
        if ($SmokeFailed) {
            Write-Warning 'Secondary error: smoke cleanup failed; runtime preserved for retry. Run stop-deployment-smoke.ps1 again.'
        } else { throw 'Smoke resource cleanup failed; runtime preserved for retry.' }
    }
}

function Wait-SmokeWebReady {
    param([scriptblock]$Probe, [ValidateRange(1, 120)][int]$TimeoutSeconds = 60,
          [ValidateRange(10, 1000)][int]$RetryMilliseconds = 500)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $remaining = [math]::Max(1, [math]::Min(5, [math]::Ceiling($TimeoutSeconds - $timer.Elapsed.TotalSeconds)))
        try { if (& $Probe $remaining) { return } } catch { }
        if ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) { Start-Sleep -Milliseconds $RetryMilliseconds }
    } while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    throw "Synthetic web readiness timed out after $TimeoutSeconds seconds (HTTPS Angular bundle probe)."
}
function Get-SmokeIdentity {
    param([string]$Root)
    $canonical = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar).ToLowerInvariant()
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $suffix = ([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($canonical)))).Replace('-', '').Substring(0, 12).ToLowerInvariant() }
    finally { $hash.Dispose() }
    [pscustomobject]@{ Project = "hfpos-dev531-$suffix"; Context = $(if ($env:OS -eq 'Windows_NT') { 'desktop-linux' } else { 'default' }) }
}
