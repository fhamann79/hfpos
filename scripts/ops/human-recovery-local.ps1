param(
    [Parameter(Mandatory=$true)][ValidateSet('Ntfs','Delete')][string]$Action,
    [Parameter(Mandatory=$true)][string]$LiteralPath
)
$ErrorActionPreference = 'Stop'
try {
    $path = [IO.Path]::GetFullPath($LiteralPath)
    $item = Get-Item -LiteralPath $path -Force
    $cursor = $item
    while ($null -ne $cursor) {
        if (($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { exit 1 }
        if ($cursor -is [IO.DirectoryInfo]) { $cursor = $cursor.Parent }
        else { $cursor = $cursor.Directory }
    }
    if ($path.StartsWith('\\') -or $path.Length -lt 3) { exit 1 }
    $volume = New-Object IO.DriveInfo([IO.Path]::GetPathRoot($path))
    if ($volume.DriveFormat -ne 'NTFS' -or $volume.DriveType -ne [IO.DriveType]::Fixed) { exit 1 }
    if ($Action -eq 'Ntfs') { Write-Output 'NTFS'; exit 0 }
    if ($item -isnot [IO.DirectoryInfo] -or $item.Name -notmatch '^human-recovery-[a-f0-9]{32}$') { exit 1 }
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    foreach ($entry in @($item) + @(Get-ChildItem -LiteralPath $path -Recurse -Force)) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { exit 1 }
        $acl = Get-Acl -LiteralPath $entry.FullName
        if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -ne $sid) { exit 1 }
        foreach ($rule in $acl.Access) {
            if ($rule.AccessControlType -eq 'Allow') {
                $allowed = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
                if ($allowed -ne $sid -and $allowed -ne 'S-1-5-18') { exit 1 }
            }
        }
    }
    Remove-Item -LiteralPath $path -Recurse -Force
    if (Test-Path -LiteralPath $path) { exit 1 }
    exit 0
} catch { exit 1 }
