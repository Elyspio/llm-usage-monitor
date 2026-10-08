# Builds the deployable artifact (on the dev machine or in the release workflow): one self-contained executable embedding the
# SPA and the default settings, for the target platform (linux/amd64: the LXC, linux/arm64: the Raspberry Pi 4).
# Driven by docker-bake.hcl: docker buildx bake [artifact|artifact-arm64]. Both stages run on the build platform: the API
# is cross-published, nothing runs under emulation.

FROM --platform=$BUILDPLATFORM node:26-bookworm-slim AS front
RUN npm install --global pnpm@12.5.1
WORKDIR /src/LlmUsageMonitor.Front
COPY LlmUsageMonitor.Front/package.json LlmUsageMonitor.Front/pnpm-lock.yaml LlmUsageMonitor.Front/pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile
COPY LlmUsageMonitor.Front/ ./
RUN pnpm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS api
ARG TARGETARCH
# The release tag (docker-bake.hcl VERSION), shown by the SPA.
ARG VERSION=0.0.0-dev
WORKDIR /src
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY LlmUsageMonitor.Api/ LlmUsageMonitor.Api/
# Embedded in the executable (SpaRoot), without the development conf.js: the API serves its own.
COPY --from=front /src/LlmUsageMonitor.Front/dist /src/spa
# The OpenAPI document is generated on the dev machine and committed; the container build does not run the application.
RUN case "$TARGETARCH" in amd64) rid=linux-x64 ;; arm64) rid=linux-arm64 ;; *) echo "Unsupported architecture $TARGETARCH" >&2; exit 1 ;; esac \
	&& dotnet publish LlmUsageMonitor.Api/LlmUsageMonitor.WebApi/LlmUsageMonitor.WebApi.csproj \
	--configuration Release \
	--runtime $rid \
	--self-contained \
	--output /out \
	-p:Version=$VERSION \
	-p:SpaRoot=/src/spa \
	-p:PublishSingleFile=true \
	-p:InvariantGlobalization=true \
	-p:OpenApiGenerateDocumentsOnBuild=false

# The executable alone: the SPA and appsettings.json are inside it, the .pdb files and the other published files stay out.
FROM scratch AS artifact
COPY --from=api /out/LlmUsageMonitor.WebApi /
