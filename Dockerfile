# ---- Stage 1: static-export the SPA (same recipe as build-desktop.sh) ----
FROM node:22-alpine AS web
WORKDIR /src/web
COPY wretched-whispers-web/package.json wretched-whispers-web/package-lock.json ./
RUN npm ci
COPY wretched-whispers-web/ ./
ENV NEXT_EXPORT=1 NEXT_PUBLIC_DESKTOP=1 NEXT_PUBLIC_API_URL=""
RUN npm run build

# ---- Stage 2: publish the API in the desktop flavor (framework-dependent) ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY wretched-whispers-server/ wretched-whispers-server/
RUN dotnet publish wretched-whispers-server/WretchedWhispers.Api/WretchedWhispers.Api.csproj \
    -c Release -p:DesktopBuild=true -o /app/publish
COPY --from=web /src/web/out/ /app/publish/wwwroot/

# ---- Stage 3: runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
# curl only for the container healthcheck; aspnet base image ships without it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=api /app/publish/ ./
ENV WW_HEADLESS=1 WW_DATA_DIR=/data
VOLUME /data
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=15s \
    CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "WretchedWhispers.Api.dll"]
