param(
    [string]$SitePath = 'C:\inetpub\wwwroot\ProjectTracking',
    [string]$SettingsPath = 'C:\inetpub\security-data\ProjectTracking\server-settings.json',
    [string]$AppPool = 'DefaultAppPool'
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $SettingsPath) {
    throw 'server-settings.json already exists. Keep this file; no migration is required.'
}
$settings = @{}
[xml]$config = Get-Content -LiteralPath (Join-Path $SitePath 'web.config')
foreach ($entry in $config.configuration.'system.webServer'.aspNetCore.environmentVariables.environmentVariable) {
    if ($entry.name -in @('SECURITY_WORKER_KEY', 'SECURITY_STORAGE_PATH', 'SECURITY_AUTO_CREATE_SCHEMA')) {
        $settings[[string]$entry.name] = [string]$entry.value
    }
}
if ([string]::IsNullOrWhiteSpace($settings['SECURITY_WORKER_KEY'])) {
    $keyPath = 'C:\inetpub\security-data\ProjectTracking\iis-worker-key.txt'
    if (Test-Path -LiteralPath $keyPath) { $settings['SECURITY_WORKER_KEY'] = [IO.File]::ReadAllText($keyPath).Trim() }
}
if ([string]::IsNullOrWhiteSpace($settings['SECURITY_WORKER_KEY']) -or $settings['SECURITY_WORKER_KEY'].Length -lt 32) {
    throw 'No valid existing worker key found. Restore the original key before migration; do not generate a new one.'
}
if (!$settings['SECURITY_STORAGE_PATH']) { $settings['SECURITY_STORAGE_PATH'] = 'C:\inetpub\security-data\ProjectTracking' }
if (!$settings['SECURITY_AUTO_CREATE_SCHEMA']) { $settings['SECURITY_AUTO_CREATE_SCHEMA'] = 'false' }
$directory = Split-Path -Parent $SettingsPath
if (!(Test-Path -LiteralPath $directory)) { throw 'Create the protected security-data directory and set its permissions first.' }
# Create the file with a restricted ACL before writing any secret content.
[IO.File]::WriteAllText($SettingsPath, '', (New-Object Text.UTF8Encoding($false)))
$acl = New-Object Security.AccessControl.FileSecurity
$acl.SetAccessRuleProtection($true, $false)
foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {
    $identity = New-Object Security.Principal.SecurityIdentifier($sid)
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', 'Allow')))
}
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule("IIS AppPool\$AppPool", 'Read', 'Allow')))
Set-Acl -LiteralPath $SettingsPath -AclObject $acl
[IO.File]::WriteAllText($SettingsPath, ($settings | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
Write-Host 'Saved persistent scanner settings. No keys were changed or displayed.'
Write-Host 'Deploy the updated application, then recycle its application pool.'
