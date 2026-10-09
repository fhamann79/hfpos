param([Parameter(Mandatory=$true)][string]$LiteralPath)
$ErrorActionPreference = 'Stop'
try {
    $item = Get-Item -LiteralPath $LiteralPath -Force
    $cursor = $item
    while ($null -ne $cursor) {
        if (($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { exit 1 }
        if ($cursor -is [IO.DirectoryInfo]) { $cursor = $cursor.Parent }
        else { $cursor = $cursor.Directory }
    }
    $acl = Get-Acl -LiteralPath $LiteralPath
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -ne $identity.User.Value) { exit 1 }
    foreach ($rule in $acl.Access) {
        if ($rule.AccessControlType -eq 'Allow') {
            $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
            if ($sid -ne $identity.User.Value -and $sid -ne 'S-1-5-18') { exit 1 }
        }
    }
    exit 0
} catch { exit 1 }
