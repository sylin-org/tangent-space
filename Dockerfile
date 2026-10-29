# The published shape in a container: one self-contained executable with its appsettings.json, listening on 5220,
# with its data folder inside the /state volume. Build it with `docker compose build`; the pinned Koan checkout
# must be at .local/upstream/koan-framework first (`pwsh scripts/tangent.ps1 build` puts it there).
FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:4ea6fe75dd36706bb6d8c3c293d4c4315840f5d76ea28ac97def77e3ec487fa5 AS build
WORKDIR /work
COPY global.json Directory.Build.props Directory.Build.targets ./
COPY src ./src
COPY .local/upstream/koan-framework ./.local/upstream/koan-framework
RUN dotnet publish src/server/Tangent.Server.csproj -c Release -r linux-x64 --self-contained true -o /publish \
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:DebugType=None -p:DebugSymbols=false -p:GenerateDocumentationFile=false \
        -p:IsTransformWebConfigDisabled=true -p:EnableSourceControlManagerQueries=false -p:EnableSourceLink=false \
    && mkdir -p /state

# Chiseled: no shell, no package manager, and a non-root account by default.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0.12-noble-chiseled-extra@sha256:566a1d5613279cf96a70bc456b85a5e1dda0cc53f394209680d54b55e4d13cf7 AS runtime
WORKDIR /app
COPY --from=build /publish ./
COPY --from=build --chown=$APP_UID:$APP_UID /state /state
# The base image's own port setting, which the server honours.
ENV ASPNETCORE_HTTP_PORTS=5220 \
    Tangent__DataFolder=/state/data
EXPOSE 5220
VOLUME /state
ENTRYPOINT ["/app/Tangent.Server"]
