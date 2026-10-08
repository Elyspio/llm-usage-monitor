# The remote root shell is fish, and PowerShell terminates a piped string with CRLF, which bash would read as part of the
# last command: scripts travel base64-encoded and are decoded into bash on the host.
function Invoke-Remote([string]$Target, [string]$Script, [string]$Failure = "remote command failed") {
	$encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($Script -replace "`r", "")))
	ssh $Target "echo $encoded | base64 -d | bash -s"
	if ($LASTEXITCODE -ne 0) { throw $Failure }
}

Export-ModuleMember -Function Invoke-Remote
