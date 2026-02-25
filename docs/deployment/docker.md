# Docker

Run dotnet-mcp-server and a SQL Server 2022 database together with a single command — no .NET SDK required on the host machine.

---

## Quick Start

**Step 1:** Copy the example files:

```bash
cp docker/appsettings.example.json docker/appsettings.json
cp .env.example .env
```

**Step 2:** Set a strong SQL Server password in `.env`:

```
SQL_SA_PASSWORD=YourStrong!Password123
```

**Step 3:** Edit `docker/appsettings.json` to configure your allowed paths, SQL connections, and HTTP hosts. The SQL connection string should use `sqlserver` as the hostname (the docker-compose service name):

```json
{
  "Sql": {
    "Connections": {
      "Local": {
        "ConnectionString": "Server=sqlserver;Database=MyDB;User Id=sa;Password=YourStrong!Password123;TrustServerCertificate=True;",
        "Description": "SQL Server in Docker"
      }
    }
  }
}
```

**Step 4:** Build and start:

```bash
docker-compose up --build
```

!!! warning "Tests run on every build"
    The multi-stage Dockerfile runs the full test suite during the build. If any test fails, the build is aborted. This ensures the published image is always tested.

---

## What docker-compose Starts

| Service | Image | Purpose |
|---------|-------|---------|
| `mcp-server` | Built from `Dockerfile` | The MCP server binary |
| `sqlserver` | `mcr.microsoft.com/mssql/server:2022-latest` | SQL Server 2022 |

The `mcp-server` container waits for `sqlserver` to pass a health check before starting.

---

## Dockerfile Architecture

The Dockerfile uses a multi-stage build:

```
Stage 1: sdk        → restore + build (full .NET SDK)
Stage 2: test       → run dotnet test (must pass)
Stage 3: publish    → dotnet publish --self-contained
Stage 4: runtime    → Alpine-based runtime image (minimal)
```

The final image is Alpine-based and contains only the self-contained binary — no .NET runtime or SDK.

---

## Connecting Claude Desktop to the Docker Container

```json
{
  "mcpServers": {
    "dotnet-mcp-server": {
      "command": "docker",
      "args": [
        "run", "--rm", "-i",
        "-v", "/absolute/path/to/docker/appsettings.json:/home/app/.config/dotnet-mcp-server/appsettings.json:ro",
        "dotnet-mcp-server"
      ]
    }
  }
}
```

!!! tip "Windows paths"
    On Windows, use forward slashes or double-escaped backslashes in the volume mount path:
    ```
    "C:/Users/YourName/project/docker/appsettings.json:/home/app/.config/dotnet-mcp-server/appsettings.json:ro"
    ```

---

## Config File Location Inside the Container

The container expects the config at:

```
/home/app/.config/dotnet-mcp-server/appsettings.json
```

This matches the Linux global tool convention. Mount your `docker/appsettings.json` to this path.

---

## Logs

Logs are written to stderr. View them with:

```bash
docker-compose logs -f mcp-server
```
