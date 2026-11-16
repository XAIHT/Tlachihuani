# Tlachihuani — Angela López Mendoza · XAIHT
# Build context: repository root. This image remains development-loopback only.
FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
ENV DOTNET_CLI_HOME=/build-home NUGET_PACKAGES=/nuget/packages NUGET_HTTP_CACHE_PATH=/nuget/http NUGET_SCRATCH=/nuget/scratch DOTNET_GENERATE_ASPNET_CERTIFICATE=false
WORKDIR /src
COPY Directory.Build.props global.json ./
COPY src/Tlachihuani.RC.Cloud/ src/Tlachihuani.RC.Cloud/
COPY src/Tlachihuani.RC.Server/wwwroot/ src/Tlachihuani.RC.Server/wwwroot/
RUN dotnet restore src/Tlachihuani.RC.Cloud/Tlachihuani.RC.Cloud.csproj --locked-mode
RUN dotnet publish src/Tlachihuani.RC.Cloud/Tlachihuani.RC.Cloud.csproj --configuration Release --runtime linux-x64 --self-contained false --no-restore --output /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4
WORKDIR /app
COPY --from=build /out/ ./
USER $APP_UID
ENTRYPOINT ["dotnet", "Tlachihuani.RC.Cloud.dll"]
