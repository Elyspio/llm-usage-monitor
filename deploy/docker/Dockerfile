# Builds the deployable artifact on the dev machine: a self-contained API with the SPA in wwwroot, for the target platform
# (linux/amd64: the LXC, linux/arm64: the Raspberry Pi 4). Driven by docker-bake.hcl: docker buildx bake [artifact|artifact-arm64].
# Both stages run on the build platform: the API is cross-published, nothing runs under emulation.

FROM --platform=$BUILDPLATFORM node:26-bookworm-slim AS front
RUN npm install --global pnpm@12.5.1
WORKDIR /src/LlmUsageMonitor.Front
COPY LlmUsageMonitor.Front/package.json LlmUsageMonitor.Front/pnpm-lock.yaml LlmUsageMonitor.Front/pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile
COPY LlmUsageMonitor.Front/ ./
RUN pnpm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS api
ARG TARGETARCH
WORKDIR /src
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY LlmUsageMonitor.Api/ LlmUsageMonitor.Api/
# The OpenAPI document is generated on the dev machine and committed; the container build does not run the application.
RUN case "$TARGETARCH" in amd64) rid=linux-x64 ;; arm64) rid=linux-arm64 ;; *) echo "Unsupported architecture $TARGETARCH" >&2; exit 1 ;; esac \
	&& dotnet publish LlmUsageMonitor.Api/LlmUsageMonitor.WebApi/LlmUsageMonitor.WebApi.csproj \
	--configuration Release \
	--runtime $rid \
	--self-contained \
	--output /out \
	-p:PublishSingleFile=true \
	-p:InvariantGlobalization=true \
	-p:OpenApiGenerateDocumentsOnBuild=false
COPY --from=front /src/LlmUsageMonitor.Front/dist /out/wwwroot
# The API serves its own /conf.js from the production settings.
RUN rm -f /out/wwwroot/conf.js /out/appsettings.Development.json

FROM scratch AS artifact
COPY --from=api /out /
