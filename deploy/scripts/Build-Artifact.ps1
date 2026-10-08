<#
.SYNOPSIS
	Builds the artifact in Docker on this machine (docker-bake.hcl) into deploy/out/<platform>, and returns its path.
.EXAMPLE
	./deploy/scripts/Build-Artifact.ps1 -Platform arm64
#>
param(
	[ValidateSet("x64", "arm64")]
	[string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$output = Join-Path $root "deploy/out/$Platform"
$target = if ($Platform -eq "arm64") { "artifact-arm64" } else { "artifact" }

if (Test-Path $output) { Remove-Item -Recurse -Force $output }
# Bake resolves the context and the output from the repository root.
Push-Location $root
try { docker buildx bake $target | Out-Host }
finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw "docker buildx bake failed" }

$output
