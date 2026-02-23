# Argha - 2026-02-23 - Multi-stage build: sdk → test → publish → runtime (Alpine)

# ── Stage 1: restore + build ─────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build

WORKDIR /src

# Copy solution and project files first for layer caching on restore
COPY McpServer.sln .
COPY src/McpServer/McpServer.csproj src/McpServer/
COPY tests/McpServer.Tests/McpServer.Tests.csproj tests/McpServer.Tests/

RUN dotnet restore

# Copy full source and build in Release
COPY . .
RUN dotnet build -c Release --no-restore

# ── Stage 2: run tests (build fails here if any test fails) ──────────────────
# Argha - 2026-02-23 - Tests run as part of docker build; failing tests abort the image
FROM build AS test

RUN dotnet test --no-build -c Release && touch /test-passed

# ── Stage 3: publish ─────────────────────────────────────────────────────────
FROM build AS publish

# Argha - 2026-02-23 - Copy sentinel file from test stage so publish depends on
# tests passing; docker will not run this stage unless the test stage succeeded
COPY --from=test /test-passed /tmp/test-passed

RUN dotnet publish src/McpServer -c Release --no-build -o /app/publish

# ── Stage 4: minimal runtime image ───────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS final

# Argha - 2026-02-23 - Run as non-root user for security
RUN addgroup -S app && adduser -S app -G app

WORKDIR /home/app

COPY --from=publish /app/publish .

USER app

# Config path resolved by Program.cs: SpecialFolder.ApplicationData on Linux
# = $HOME/.config = /home/app/.config
# Mount appsettings.json at: /home/app/.config/dotnet-mcp-server/appsettings.json
ENTRYPOINT ["dotnet", "dotnet-mcp-server.dll"]
