<#
.SYNOPSIS
	Downloads the executable of <Platform> and the systemd unit of the GitHub release v<Version> into deploy/out/release-<Version>,
	checks them against its SHA256SUMS, and returns the directory. Requires the GitHub CLI (gh).
.EXAMPLE
	./deploy/scripts/Get-Release.ps1 -Version 1.0.0 -Platform arm64
#>
param(
	[Parameter(Mandatory)]
	[string]$Version,
	[ValidateSet("x64", "arm64")]
	[string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tag = "v$($Version.TrimStart('v'))"
$output = Join-Path $root "deploy/out/release-$($tag.Substring(1))"
$executable = "llm-usage-monitor-linux-$Platform"
$assets = @($executable, "llm-usage-monitor.service", "SHA256SUMS")

if (Test-Path $output) { Remove-Item -Recurse -Force $output }
$patterns = $assets | ForEach-Object { "--pattern", $_ }
gh release download $tag --repo Elyspio/llm-usage-monitor --dir $output @patterns
if ($LASTEXITCODE -ne 0) { throw "Download of the release $tag failed" }

# SHA256SUMS lists every asset of the release: "<hash>  <name>".
$expected = @{}
foreach ($line in Get-Content (Join-Path $output "SHA256SUMS")) {
	if ($line -match '^([0-9a-f]{64})\s+\*?(.+)$') { $expected[$Matches[2]] = $Matches[1] }
}
foreach ($asset in $assets | Where-Object { $_ -ne "SHA256SUMS" }) {
	$actual = (Get-FileHash (Join-Path $output $asset) -Algorithm SHA256).Hash.ToLowerInvariant()
	if ($expected[$asset] -ne $actual) { throw "$asset of $tag does not match SHA256SUMS" }
}

$output
