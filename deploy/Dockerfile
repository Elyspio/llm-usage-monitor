# Builds the deployable artifact on the dev machine: a self-contained linux-x64 API with the SPA in wwwroot.
# docker build -f deploy/Dockerfile --target artifact --output type=local,dest=deploy/out .

FROM node:24-bookworm-slim AS front
RUN npm install --global pnpm@11.25.0
WORKDIR /src/LlmUsageMonitor.Front
COPY LlmUsageMonitor.Front/package.json LlmUsageMonitor.Front/pnpm-lock.yaml LlmUsageMonitor.Front/pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile
COPY LlmUsageMonitor.Front/ ./
RUN pnpm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY global.json nuget.config Directory.Build.props ./
COPY LlmUsageMonitor.Api/ LlmUsageMonitor.Api/
# The OpenAPI document is generated on the dev machine and committed; the container build does not run the application.
RUN dotnet publish LlmUsageMonitor.Api/LlmUsageMonitor.WebApi/LlmUsageMonitor.WebApi.csproj \
	--configuration Release \
	--runtime linux-x64 \
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
