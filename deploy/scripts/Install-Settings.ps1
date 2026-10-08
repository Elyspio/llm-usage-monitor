<#
.SYNOPSIS
	Installs a local settings file on the host, for the first deployment or after a configuration change.
.EXAMPLE
	./deploy/scripts/Install-Settings.ps1 -Target root@ely-llm-wake-up.elylan -Path deploy/config/appsettings.Production.json
#>
param(
	[Parameter(Mandatory)]
	[string]$Target,
	[Parameter(Mandatory)]
	[string]$Path,
	[string]$SettingsFile = "/etc/llm-usage-monitor/appsettings.Production.json"
)

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "Remote.psm1") -Force

if (-not (Test-Path $Path)) { throw "No settings file at ${Path}." }
# The file holds secrets: it transits through a directory only root can read (scp keeps the local, readable mode).
$uploadDirectory = (Invoke-Remote $Target "umask 077 && mktemp -d" "remote creation of the upload directory failed" | Select-Object -Last 1).Trim()
scp $Path "${Target}:$uploadDirectory/appsettings.Production.json"
if ($LASTEXITCODE -ne 0) {
	Invoke-Remote $Target "rm -rf $uploadDirectory"
	throw "scp of the settings failed"
}
$settingsDirectory = $SettingsFile.Substring(0, $SettingsFile.LastIndexOf("/"))
Invoke-Remote $Target @"
set -e
umask 077
trap 'rm -rf $uploadDirectory' EXIT
install -d -o root -g llm-monitor -m 750 $settingsDirectory
install -o llm-monitor -g llm-monitor -m 600 $uploadDirectory/appsettings.Production.json $SettingsFile
"@ "remote installation of the settings failed"
