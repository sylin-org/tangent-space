FROM node:24.15.0-bookworm-slim@sha256:4e6b70dd6cbfc88c8157ba19aa3d9f9cce6ba4703576d55459e45efcbc9c5f5d AS frontend
WORKDIR /work/src/client
COPY src/client/package.json src/client/package-lock.json ./
RUN npm ci
COPY src/client ./
COPY src/server/web/wwwroot /work/src/server/web/wwwroot
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:4ea6fe75dd36706bb6d8c3c293d4c4315840f5d76ea28ac97def77e3ec487fa5 AS build
WORKDIR /work
COPY global.json ./
COPY src ./src
COPY .local/upstream/koan-framework ./.local/upstream/koan-framework
RUN rm -rf src/server/web/wwwroot
COPY --from=frontend /work/src/server/web/wwwroot ./src/server/web/wwwroot
RUN dotnet publish src/server/web/TangentSpace.csproj -c Release -r linux-x64 --self-contained false -o /publish \
    -p:EnableSourceControlManagerQueries=false -p:EnableSourceLink=false \
    && cp src/server/web/koan.lock.json /publish/koan.lock.json

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:1fe86375600b62e6566b465da9553eef0621f13c67f40fe764cd8dbb1dee1497 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /publish ./
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    LANG=C.UTF-8 \
    LC_ALL=C.UTF-8
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TangentSpace.dll", "--contentRoot", "/state", "--webroot", "/app/wwwroot"]
