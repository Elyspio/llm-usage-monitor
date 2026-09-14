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
	[switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $PSScriptRoot "out"
$archive = Join-Path $PSScriptRoot "llm-usage-monitor.tar.gz"

if (-not $SkipBuild) {
	if (Test-Path $output) { Remove-Item -Recurse -Force $output }
	docker build --file (Join-Path $PSScriptRoot "Dockerfile") --target artifact --output "type=local,dest=$output" $root
	if ($LASTEXITCODE -ne 0) { throw "docker build failed" }
}

if (-not (Test-Path (Join-Path $output "LlmUsageMonitor.WebApi"))) { throw "No artifact in ${output}: run without -SkipBuild." }

tar -czf $archive -C $output .
if ($LASTEXITCODE -ne 0) { throw "tar failed" }

scp $archive "${Target}:/tmp/llm-usage-monitor.tar.gz"
if ($LASTEXITCODE -ne 0) { throw "scp failed" }

$remote = @"
set -e
systemctl stop llm-usage-monitor || true
mkdir -p $InstallDirectory
tar -xzf /tmp/llm-usage-monitor.tar.gz -C $InstallDirectory
chmod 755 $InstallDirectory/LlmUsageMonitor.WebApi
rm -f /tmp/llm-usage-monitor.tar.gz
systemctl start llm-usage-monitor
sleep 3
systemctl --no-pager --lines=10 status llm-usage-monitor
"@
ssh $Target ($remote -replace "`r", "")
if ($LASTEXITCODE -ne 0) { throw "remote restart failed" }

Write-Host "Deployed to ${Target}:$InstallDirectory"
