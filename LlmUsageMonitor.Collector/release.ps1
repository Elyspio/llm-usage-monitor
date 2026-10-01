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

$version = (Get-Content package.json -Raw | ConvertFrom-Json).version
$tag = "collector-v$version"

if (-not $DryRun) {
	if (git status --porcelain -- .) { throw "Uncommitted changes in LlmUsageMonitor.Collector: commit the release first." }
	gh release view $tag *> $null
	if ($LASTEXITCODE -eq 0) { throw "Release $tag already exists: bump the version in package.json." }
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

Invoke-Step "npm publish" { npm publish --access public }
Invoke-Step "GitHub release" {
	gh release create $tag @assets --title "LLM Usage Collector $version" --notes "Standalone collector $version: download the executable of your platform, then run ``install`` and ``login`` (see LlmUsageMonitor.Collector/README.md)."
}
Write-Host "Published @elyspio/llm-usage-collector@$version and $tag." -ForegroundColor Green
