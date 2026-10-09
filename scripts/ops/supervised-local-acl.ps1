param([Parameter(Mandatory=$true)][string]$LiteralPath, [switch]$CreateFile, [switch]$CreateDirectory)
$ErrorActionPreference = 'Stop'
try {
    if ($CreateFile -and $CreateDirectory) { exit 1 }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if ($CreateFile -or $CreateDirectory) {
        if (Test-Path -LiteralPath $LiteralPath) { exit 1 }
        $parent = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($LiteralPath))
        & $PSCommandPath -LiteralPath $parent
        if ($LASTEXITCODE -ne 0) { exit 1 }
        if ($CreateDirectory) {
            $security = New-Object Security.AccessControl.DirectorySecurity
            $security.SetOwner($identity.User)
            $security.SetAccessRuleProtection($true, $false)
            foreach ($sid in @($identity.User, (New-Object Security.Principal.SecurityIdentifier('S-1-5-18')))) {
                $security.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow')))
            }
            $null = [IO.Directory]::CreateDirectory($LiteralPath, $security)
        } else {
            $security = New-Object Security.AccessControl.FileSecurity
            $security.SetOwner($identity.User)
            $security.SetAccessRuleProtection($true, $false)
            foreach ($sid in @($identity.User, (New-Object Security.Principal.SecurityIdentifier('S-1-5-18')))) {
                $security.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','Allow')))
            }
            $stream = New-Object IO.FileStream($LiteralPath,[IO.FileMode]::CreateNew,[Security.AccessControl.FileSystemRights]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::None,$security)
            $stream.Dispose()
        }
    }
    $item = Get-Item -LiteralPath $LiteralPath -Force
    $cursor = $item
    while ($null -ne $cursor) {
        if (($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { exit 1 }
        if ($cursor -is [IO.DirectoryInfo]) { $cursor = $cursor.Parent }
        else { $cursor = $cursor.Directory }
    }
    $acl = Get-Acl -LiteralPath $LiteralPath
    if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -ne $identity.User.Value) { exit 1 }
    foreach ($rule in $acl.Access) {
        if ($rule.AccessControlType -eq 'Allow') {
            $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
            if ($sid -ne $identity.User.Value -and $sid -ne 'S-1-5-18') { exit 1 }
        }
    }
    exit 0
} catch { exit 1 }
