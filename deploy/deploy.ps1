<#
.SYNOPSIS
	Builds the artifact in Docker on this machine, copies it to the host and restarts the service (in-place overwrite).
	-Platform: x64 for the LXC, arm64 for the Raspberry Pi 4 (-Target required). Steps in scripts/.
.EXAMPLE
	./deploy/deploy.ps1
	./deploy/deploy.ps1 -Target root@ely-llm-wake-up.elylan
	./deploy/deploy.ps1 -Platform arm64 -Target root@raspberrypi.elylan
#>
param(
	[ValidateSet("x64", "arm64")]
	[string]$Platform = "x64",
	[string]$Target = "root@ely-llm-wake-up.elylan",
	[string]$InstallDirectory = "/opt/llm-usage-monitor",
	[string]$SettingsFile = "/etc/llm-usage-monitor/appsettings.Production.json",
	# Local settings file to install on the host, for the first deployment or after a configuration change.
	[string]$UploadSettings,
	[switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$scripts = Join-Path $PSScriptRoot "scripts"

if ($Platform -eq "arm64" -and -not $PSBoundParameters.ContainsKey("Target")) { throw "-Platform arm64 deploys to the Raspberry Pi: set -Target." }

if ($SkipBuild) { $artifact = Join-Path $PSScriptRoot "out/$Platform" }
else { $artifact = & "$scripts/Build-Artifact.ps1" -Platform $Platform }

if ($UploadSettings) {
	& "$scripts/Install-Settings.ps1" -Target $Target -Path $UploadSettings -SettingsFile $SettingsFile
}

& "$scripts/Install-Release.ps1" -Target $Target -Artifact $artifact -InstallDirectory $InstallDirectory -SettingsFile $SettingsFile
