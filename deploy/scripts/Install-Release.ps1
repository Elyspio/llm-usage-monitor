<#
.SYNOPSIS
	Copies a built artifact and the systemd unit to the host, extracts it (in-place overwrite) and restarts the service.
.EXAMPLE
	./deploy/scripts/Install-Release.ps1 -Target root@ely-llm-wake-up.elylan -Artifact deploy/out/x64
#>
param(
	[Parameter(Mandatory)]
	[string]$Target,
	[Parameter(Mandatory)]
	[string]$Artifact,
	[string]$InstallDirectory = "/opt/llm-usage-monitor",
	[string]$SettingsFile = "/etc/llm-usage-monitor/appsettings.Production.json"
)

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "Remote.psm1") -Force
$archive = "$Artifact.tar.gz"
$unit = Join-Path (Split-Path -Parent $PSScriptRoot) "systemd/llm-usage-monitor.service"

if (-not (Test-Path (Join-Path $Artifact "LlmUsageMonitor.WebApi"))) { throw "No artifact in ${Artifact}: build it first." }

# The settings file holds the Mongo password: it is uploaded on demand, and never committed.
Invoke-Remote $Target "test -s $SettingsFile" "Missing or empty $SettingsFile on ${Target}: deploy once with -UploadSettings, see deploy/README.md."

tar -czf $archive -C $Artifact .
if ($LASTEXITCODE -ne 0) { throw "tar failed" }

scp $archive "${Target}:/tmp/llm-usage-monitor.tar.gz"
if ($LASTEXITCODE -ne 0) { throw "scp failed" }

scp $unit "${Target}:/tmp/llm-usage-monitor.service"
if ($LASTEXITCODE -ne 0) { throw "scp of the unit failed" }

Invoke-Remote $Target @"
set -e
install -m 644 /tmp/llm-usage-monitor.service /etc/systemd/system/llm-usage-monitor.service
systemctl daemon-reload
systemctl enable llm-usage-monitor >/dev/null
systemctl stop llm-usage-monitor || true
mkdir -p $InstallDirectory
tar -xzf /tmp/llm-usage-monitor.tar.gz -C $InstallDirectory
chmod 755 $InstallDirectory/LlmUsageMonitor.WebApi
rm -f /tmp/llm-usage-monitor.tar.gz /tmp/llm-usage-monitor.service
# A deployment is a deliberate start: it clears a start limit reached by a previous crash loop.
systemctl reset-failed llm-usage-monitor || true
systemctl start llm-usage-monitor
# The process must answer its liveness probe; the readiness is printed only, a degraded provider does not fail the deployment.
if ! curl -fsS --retry 15 --retry-delay 2 --retry-all-errors --max-time 5 -o /dev/null http://127.0.0.1:5000/health/live; then
	systemctl --no-pager --lines=40 status llm-usage-monitor
	exit 1
fi
echo "readiness: `$(curl -sS --max-time 15 http://127.0.0.1:5000/health/ready)"
systemctl --no-pager --lines=20 status llm-usage-monitor
"@ "deployment on the host failed (or the service does not answer /health/live): see the output above"

Write-Host "Deployed to ${Target}:$InstallDirectory"
