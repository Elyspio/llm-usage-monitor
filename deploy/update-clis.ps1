<#
.SYNOPSIS
	Updates the claude and codex CLIs of the service account to their latest version, both at once, then restarts the service
	so that no process of the previous version stays running (the Codex app-server).
.EXAMPLE
	./deploy/update-clis.ps1
	./deploy/update-clis.ps1 -Target root@raspberrypi.elylan
#>
param(
	[string]$Target = "root@ely-llm-wake-up.elylan",
	[string]$Account = "llm-monitor"
)

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "scripts/Remote.psm1") -Force

Invoke-Remote $Target @"
set -e
# The installers run from a directory the account can read: from /root, find fails and the update is left half done.
cd /tmp
logs=`$(mktemp -d)
run() { sudo -u $Account -H bash -c "cd /tmp && `$2" >"`$logs/`$1.log" 2>&1; }
version() { sudo -u $Account -H bash -lc "cd /tmp && `$1 --version" 2>&1 || echo "not installed"; }

echo "before: claude `$(version claude), codex `$(version codex)"
run claude 'curl -fsSL https://claude.ai/install.sh | bash -s latest' & claude=`$!
run codex 'curl -fsSL https://chatgpt.com/codex/install.sh | sh' & codex=`$!
status=0
wait `$claude || { echo "claude update failed:"; cat "`$logs/claude.log"; status=1; }
wait `$codex || { echo "codex update failed:"; cat "`$logs/codex.log"; status=1; }
rm -rf "`$logs"
echo "after: claude `$(version claude), codex `$(version codex)"
[ `$status -eq 0 ]

systemctl restart llm-usage-monitor
if ! curl -fsS --retry 15 --retry-delay 2 --retry-all-errors --max-time 5 -o /dev/null http://127.0.0.1:5000/health/live; then
	systemctl --no-pager --lines=40 status llm-usage-monitor
	exit 1
fi
echo "readiness: `$(curl -sS --max-time 15 http://127.0.0.1:5000/health/ready)"
"@ "CLI update on the host failed (or the service does not answer /health/live): see the output above"

Write-Host "CLIs updated on $Target"
