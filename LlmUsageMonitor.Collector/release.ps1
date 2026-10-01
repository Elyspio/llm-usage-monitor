# Publishes the collector: npm package and GitHub release collector-v<version> with the executables.
# No CI in this repository: run it from Windows, signed in to npm (npm login) and GitHub (gh auth login).
# Bump "version" in package.json and commit it first.
param(
	# Builds and checks everything without publishing.
	[switch]$DryRun
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Invoke-Step([string]$Name, [scriptblock]$Command) {
	Write-Host "==> $Name" -ForegroundColor Cyan
	& $Command
	if ($LASTEXITCODE -ne 0) { throw "$Name failed ($LASTEXITCODE)" }
}

$package = Get-Content package.json -Raw | ConvertFrom-Json
$version = $package.version
$tag = "collector-v$version"

# Each target is checked on its own, so a release interrupted halfway can be resumed.
# npm view prints nothing (or E404 for a never-published package) when the version is missing.
$npmPublished = [bool](npm view "$($package.name)@$version" version 2> $null)
gh release view $tag *> $null
$ghReleased = $LASTEXITCODE -eq 0
$global:LASTEXITCODE = 0

if (-not $DryRun) {
	if ($npmPublished -and $ghReleased) {
		Write-Host "$($package.name)@$version and $tag already published: bump the version in package.json." -ForegroundColor Yellow
		return
	}
	if (git status --porcelain -- .) { throw "Uncommitted changes in LlmUsageMonitor.Collector: commit the release first." }
}

Invoke-Step "Install" { pnpm install --frozen-lockfile }
Invoke-Step "Check" { pnpm check }
Invoke-Step "Test" { pnpm test }
Invoke-Step "Build library" { pnpm build }
# Windows tar (System32) extracts the Node.js archives; the Git Bash one cannot.
Invoke-Step "Build executables" { pnpm build:exe }

$assets = @("build/llm-usage-win-x64.exe", "build/llm-usage-linux-x64")
foreach ($asset in $assets) { if (-not (Test-Path $asset)) { throw "Missing $asset" } }

Invoke-Step "Smoke test" { & ./build/llm-usage-win-x64.exe --version }

if ($DryRun) {
	Invoke-Step "npm pack (dry run)" { npm publish --access public --dry-run }
	Write-Host "Dry run: $tag not published." -ForegroundColor Yellow
	return
}

if ($npmPublished) {
	Write-Host "==> npm publish skipped: $($package.name)@$version already on npm" -ForegroundColor Yellow
} else {
	Invoke-Step "npm publish" { npm publish --access public }
}

if ($ghReleased) {
	Write-Host "==> GitHub release skipped: $tag already exists" -ForegroundColor Yellow
} else {
	Invoke-Step "GitHub release" {
		gh release create $tag @assets --title "LLM Usage Collector $version" --notes "Standalone collector ${version}: download the executable of your platform, then run ``install`` and ``login`` (see LlmUsageMonitor.Collector/README.md)."
	}
}
Write-Host "Published $($package.name)@$version and $tag." -ForegroundColor Green
