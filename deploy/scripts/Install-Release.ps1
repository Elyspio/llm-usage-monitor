<#
.SYNOPSIS
	Copies the executable and the systemd unit to the host, replaces the installed application and restarts the service.
	The executable embeds the SPA and the default settings: the install directory keeps it alone (the wwwroot, appsettings
	and .pdb files of the archives installed before are removed).
.EXAMPLE
	./deploy/scripts/Install-Release.ps1 -Target root@ely-llm-wake-up.elylan -Executable deploy/out/x64/LlmUsageMonitor.WebApi
#>
param(
	[Parameter(Mandatory)]
	[string]$Target,
	[Parameter(Mandatory)]
	[string]$Executable,
	# The unit of the deployed version: the one of a GitHub release, or the one of this checkout by default.
	[string]$Unit = (Join-Path (Split-Path -Parent $PSScriptRoot) "systemd/llm-usage-monitor.service"),
	[string]$InstallDirectory = "/opt/llm-usage-monitor",
	[string]$SettingsFile = "/etc/llm-usage-monitor/appsettings.Production.json"
)

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "Remote.psm1") -Force

if (-not (Test-Path $Executable -PathType Leaf)) { throw "No executable at ${Executable}: build or download it first." }
if (-not (Test-Path $Unit -PathType Leaf)) { throw "No systemd unit at $Unit." }

# The settings file holds the Mongo password: it is uploaded on demand, and never committed.
Invoke-Remote $Target "test -s $SettingsFile" "Missing or empty $SettingsFile on ${Target}: deploy once with -UploadSettings, see deploy/README.md."

# -C: the executable compresses to about a third of its size.
scp -C $Executable "${Target}:/tmp/llm-usage-monitor"
if ($LASTEXITCODE -ne 0) { throw "scp failed" }

scp $Unit "${Target}:/tmp/llm-usage-monitor.service"
if ($LASTEXITCODE -ne 0) { throw "scp of the unit failed" }

Invoke-Remote $Target @"
set -eu
install -m 644 /tmp/llm-usage-monitor.service /etc/systemd/system/llm-usage-monitor.service
systemctl daemon-reload
systemctl enable llm-usage-monitor >/dev/null
systemctl stop llm-usage-monitor || true
# 755 whatever it was: the archives installed before left it world-writable.
install -d -m 755 $InstallDirectory
# Left by the archives installed before: an appsettings.json on disk would override the defaults embedded in the executable.
find $InstallDirectory -mindepth 1 -maxdepth 1 ! -name LlmUsageMonitor.WebApi -exec rm -rf {} +
install -m 755 /tmp/llm-usage-monitor $InstallDirectory/LlmUsageMonitor.WebApi
rm -f /tmp/llm-usage-monitor /tmp/llm-usage-monitor.service
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
