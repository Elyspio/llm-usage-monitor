<#
.SYNOPSIS
	Builds the artifact in Docker on this machine, copies it to the LXC and restarts the service (in-place overwrite).
.EXAMPLE
	./deploy/deploy.ps1
	./deploy/deploy.ps1 -Target root@ely-llm-wake-up.elylan
#>
param(
	[string]$Target = "root@ely-llm-wake-up.elylan",
	[string]$InstallDirectory = "/opt/llm-usage-monitor",
	[string]$SettingsFile = "/etc/llm-usage-monitor/appsettings.Production.json",
	# Local settings file to install on the host, for the first deployment or after a configuration change.
	[string]$UploadSettings,
	[switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $PSScriptRoot "out"
$archive = Join-Path $PSScriptRoot "llm-usage-monitor.tar.gz"
$unit = Join-Path $PSScriptRoot "llm-usage-monitor.service"

# The remote root shell is fish, and PowerShell terminates a piped string with CRLF, which bash would read as part of the
# last command: scripts travel base64-encoded and are decoded into bash on the host.
function Invoke-Remote([string]$script, [string]$failure = "remote command failed") {
	$encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($script -replace "`r", "")))
	ssh $Target "echo $encoded | base64 -d | bash -s"
	if ($LASTEXITCODE -ne 0) { throw $failure }
}

if (-not $SkipBuild) {
	if (Test-Path $output) { Remove-Item -Recurse -Force $output }
	docker build --file (Join-Path $PSScriptRoot "Dockerfile") --target artifact --output "type=local,dest=$output" $root
	if ($LASTEXITCODE -ne 0) { throw "docker build failed" }
}

if (-not (Test-Path (Join-Path $output "LlmUsageMonitor.WebApi"))) { throw "No artifact in ${output}: run without -SkipBuild." }

if ($UploadSettings) {
	if (-not (Test-Path $UploadSettings)) { throw "No settings file at ${UploadSettings}." }
	scp $UploadSettings "${Target}:/tmp/appsettings.Production.json"
	if ($LASTEXITCODE -ne 0) { throw "scp of the settings failed" }
	$settingsDirectory = $SettingsFile.Substring(0, $SettingsFile.LastIndexOf("/"))
	Invoke-Remote @"
set -e
install -d -o root -g llm-monitor -m 750 $settingsDirectory
install -o llm-monitor -g llm-monitor -m 600 /tmp/appsettings.Production.json $SettingsFile
rm -f /tmp/appsettings.Production.json
"@ "remote installation of the settings failed"
}

# The settings file holds the Mongo password: it is uploaded on demand, and never committed.
Invoke-Remote "test -s $SettingsFile" "Missing or empty $SettingsFile on ${Target}: deploy once with -UploadSettings, see deploy/README.md."

tar -czf $archive -C $output .
if ($LASTEXITCODE -ne 0) { throw "tar failed" }

scp $archive "${Target}:/tmp/llm-usage-monitor.tar.gz"
if ($LASTEXITCODE -ne 0) { throw "scp failed" }

scp $unit "${Target}:/tmp/llm-usage-monitor.service"
if ($LASTEXITCODE -ne 0) { throw "scp of the unit failed" }

Invoke-Remote @"
set -e
install -m 644 /tmp/llm-usage-monitor.service /etc/systemd/system/llm-usage-monitor.service
systemctl daemon-reload
systemctl enable llm-usage-monitor >/dev/null
systemctl stop llm-usage-monitor || true
mkdir -p $InstallDirectory
tar -xzf /tmp/llm-usage-monitor.tar.gz -C $InstallDirectory
chmod 755 $InstallDirectory/LlmUsageMonitor.WebApi
rm -f /tmp/llm-usage-monitor.tar.gz /tmp/llm-usage-monitor.service
systemctl start llm-usage-monitor
sleep 5
systemctl --no-pager --lines=20 status llm-usage-monitor
"@

Write-Host "Deployed to ${Target}:$InstallDirectory"
