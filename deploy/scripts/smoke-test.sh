#!/usr/bin/env bash
# Starts a released executable alone in an empty directory, as on the host, and checks that it serves the SPA embedded in it.
# Used by the release workflow (MongoDB service container); runs on any linux-x64 machine with curl and a MongoDB.
#   deploy/scripts/smoke-test.sh <executable> <mongodb connection string> [expected version]
set -euo pipefail

executable=$(realpath "$1")
mongo=$2
version=${3:-}
port=5080
base="http://127.0.0.1:$port"

workdir=$(mktemp -d)
cp "$executable" "$workdir/LlmUsageMonitor.WebApi"
chmod +x "$workdir/LlmUsageMonitor.WebApi"
cd "$workdir"

fail() {
	echo "smoke test failed: $*" >&2
	echo "--- application output" >&2
	cat app.log >&2 || true
	exit 1
}

# Production, as on the host; the CLIs are absent, which only degrades the readiness.
ASPNETCORE_ENVIRONMENT=Production \
	ASPNETCORE_URLS="$base" \
	ConnectionStrings__MongoDB="$mongo" \
	Oidc__Authority="https://auth.smoke.test/realms/smoke" \
	App__PublicUrl="$base" \
	./LlmUsageMonitor.WebApi > app.log 2>&1 &
app=$!
trap 'kill $app 2> /dev/null || true; rm -rf "$workdir"' EXIT

curl -fsS --retry 30 --retry-delay 1 --retry-all-errors --max-time 5 -o /dev/null "$base/health/live" 2> /dev/null || fail "/health/live does not answer"

# Status, content type and body of a GET; the headers go to headers.txt.
get() {
	curl -sS --max-time 10 -D headers.txt -o body.txt -w '%{http_code} %{content_type}' "$base$1"
}

[[ $(get /) == "200 text/html"* ]] || fail "/ is not the embedded index.html"
grep -q '<div id="root">' body.txt || fail "/ does not serve the SPA shell"
grep -qi '^content-security-policy:.*script-src .self.' headers.txt || fail "/ has no content security policy"
asset=$(grep -o 'src="/assets/[^"]*\.js"' body.txt | head -n 1 | cut -d '"' -f 2)
[[ -n $asset ]] || fail "index.html references no script asset"

[[ $(get /settings) == "200 text/html"* ]] || fail "a client route does not fall back to index.html"
[[ $(get "$asset") == "200 text/javascript"* ]] || fail "$asset is not served"
[[ $(get /conf.js) == "200 text/javascript"* ]] || fail "/conf.js is not served"
grep -q 'auth.smoke.test' body.txt || fail "/conf.js does not carry the configured realm"
if [[ -n $version ]]; then
	grep -q "version: \"$version\"" body.txt || fail "/conf.js does not carry the version $version"
fi
[[ $(get /api/dashboard) == "401"* ]] || fail "/api/dashboard is not protected"

echo "smoke test passed: SPA, $asset, /conf.js${version:+ ($version)} and the API protection"
