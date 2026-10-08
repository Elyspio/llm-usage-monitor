<#
.SYNOPSIS
	Installs the application on the host and restarts the service: the executable of a GitHub release (-Version), or one
	built in Docker on this machine from the checkout (default).
	-Platform: x64 for the LXC, arm64 for the Raspberry Pi 4 (-Target required). Steps in scripts/.
.EXAMPLE
	./deploy/deploy.ps1 -Version 1.2.0
	./deploy/deploy.ps1
	./deploy/deploy.ps1 -Target root@ely-llm-wake-up.elylan
	./deploy/deploy.ps1 -Platform arm64 -Target root@raspberrypi.elylan -Version 1.2.0
#>
param(
	[ValidateSet("x64", "arm64")]
	[string]$Platform = "x64",
	[string]$Target = "root@ely-llm-wake-up.elylan",
	# A released version (1.2.0): its executable and its systemd unit, checked against the SHA256SUMS of the release.
	[string]$Version,
	[string]$InstallDirectory = "/opt/llm-usage-monitor",
	[string]$SettingsFile = "/etc/llm-usage-monitor/appsettings.Production.json",
	# Local settings file to install on the host, for the first deployment or after a configuration change.
	[string]$UploadSettings,
	# Without -Version: installs the executable built before in deploy/out/<platform>.
	[switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$scripts = Join-Path $PSScriptRoot "scripts"

if ($Platform -eq "arm64" -and -not $PSBoundParameters.ContainsKey("Target")) { throw "-Platform arm64 deploys to the Raspberry Pi: set -Target." }
if ($Version -and $SkipBuild) { throw "-SkipBuild applies to a local build: drop it with -Version." }

if ($Version) {
	$release = & "$scripts/Get-Release.ps1" -Version $Version -Platform $Platform
	$executable = Join-Path $release "llm-usage-monitor-linux-$Platform"
	$unit = Join-Path $release "llm-usage-monitor.service"
}
else {
	$artifact = if ($SkipBuild) { Join-Path $PSScriptRoot "out/$Platform" } else { & "$scripts/Build-Artifact.ps1" -Platform $Platform }
	$executable = Join-Path $artifact "LlmUsageMonitor.WebApi"
	$unit = Join-Path $PSScriptRoot "systemd/llm-usage-monitor.service"
}

if ($UploadSettings) {
	& "$scripts/Install-Settings.ps1" -Target $Target -Path $UploadSettings -SettingsFile $SettingsFile
}

& "$scripts/Install-Release.ps1" -Target $Target -Executable $executable -Unit $unit -InstallDirectory $InstallDirectory -SettingsFile $SettingsFile
