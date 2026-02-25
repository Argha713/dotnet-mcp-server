# Configuration Profiles

Ready-to-use `appsettings.json` files for three common use cases live in [`examples/configs/`](https://github.com/Argha713/dotnet-mcp-server/tree/master/examples/configs).

Copy the profile that matches your needs to your config directory and edit the values to suit your environment.

---

## Developer Profile

**File:** `examples/configs/developer.json`
**Best for:** Software developers working on local repositories with local or dev databases.

**What's configured:**

| Section | Details |
|---------|---------|
| AllowedPaths | Source repos, Documents, `C:\Projects`, `C:\Temp` |
| SQL Connections | LocalDev (SQL Server), LocalSqlite (SQLite) |
| AllowedHosts | api.github.com, raw.githubusercontent.com, registry.npmjs.org, pypi.org, nuget.org, docs.microsoft.com, learn.microsoft.com, httpbin.org |
| Logging | `McpServer=Debug` (verbose, for development) |

```json
{
  "FileSystem": {
    "AllowedPaths": [
      "C:\\Users\\YourUsername\\source\\repos",
      "C:\\Users\\YourUsername\\Documents",
      "C:\\Projects",
      "C:\\Temp"
    ]
  },
  "Sql": {
    "Connections": {
      "LocalDev": {
        "ConnectionString": "Server=localhost;Database=DevDB;Trusted_Connection=True;TrustServerCertificate=True;",
        "Description": "Local development SQL Server database"
      }
    }
  },
  "Http": {
    "AllowedHosts": [
      "api.github.com",
      "raw.githubusercontent.com",
      "registry.npmjs.org",
      "nuget.org",
      "docs.microsoft.com"
    ]
  }
}
```

---

## Data Analyst Profile

**File:** `examples/configs/data-analyst.json`
**Best for:** Data analysts working with data directories, analytics databases, and public data APIs.

**What's configured:**

| Section | Details |
|---------|---------|
| AllowedPaths | Downloads, Documents\Reports, `C:\Data`, `C:\Exports` |
| SQL Connections | DataWarehouse, Analytics, Staging |
| AllowedHosts | data.worldbank.org, api.census.gov, fred.stlouisfed.org, api.exchangerate-api.com, stats.oecd.org, and more |
| HTTP Timeout | 60 seconds (for large data API responses) |
| Logging | `Default=Warning`, `McpServer=Information` |

```json
{
  "FileSystem": {
    "AllowedPaths": [
      "C:\\Users\\YourUsername\\Downloads",
      "C:\\Users\\YourUsername\\Documents\\Reports",
      "C:\\Data",
      "C:\\Exports"
    ]
  },
  "Sql": {
    "Connections": {
      "DataWarehouse": {
        "ConnectionString": "Server=dw-server;Database=DataWarehouse;Trusted_Connection=True;",
        "Description": "Corporate data warehouse"
      }
    }
  },
  "Http": {
    "AllowedHosts": [
      "data.worldbank.org",
      "api.census.gov",
      "fred.stlouisfed.org",
      "stats.oecd.org",
      "api.exchangerate-api.com"
    ],
    "TimeoutSeconds": 60
  }
}
```

---

## API Integrator Profile

**File:** `examples/configs/api-integrator.json`
**Best for:** Integration developers connecting to external SaaS APIs — minimal filesystem access, no SQL.

**What's configured:**

| Section | Details |
|---------|---------|
| AllowedPaths | Downloads and `C:\Temp` only |
| SQL Connections | None (empty) |
| AllowedHosts | api.github.com, api.stripe.com, api.sendgrid.com, api.twilio.com, api.openai.com, hooks.slack.com, api.notion.com, api.airtable.com, graph.microsoft.com, and more |
| Plugin Config | `DefaultApiTimeout=30`, `RetryCount=3` |

```json
{
  "FileSystem": {
    "AllowedPaths": [
      "C:\\Users\\YourUsername\\Downloads",
      "C:\\Temp"
    ]
  },
  "Sql": {
    "Connections": {}
  },
  "Http": {
    "AllowedHosts": [
      "api.github.com",
      "api.stripe.com",
      "api.sendgrid.com",
      "api.twilio.com",
      "api.openai.com",
      "hooks.slack.com",
      "api.notion.com",
      "graph.microsoft.com"
    ]
  },
  "Plugins": {
    "Config": {
      "DefaultApiTimeout": "30",
      "RetryCount": "3"
    }
  }
}
```

---

## Using a Profile

**Global tool:**

=== "Windows"
    ```powershell
    copy examples\configs\developer.json "$env:APPDATA\dotnet-mcp-server\appsettings.json"
    ```

=== "Linux / macOS"
    ```bash
    cp examples/configs/developer.json ~/.config/dotnet-mcp-server/appsettings.json
    ```

Then edit the file to update paths, connection strings, and hostnames for your environment.

---

## Next Steps

- [Validate your config →](../getting-started/configuration.md#validate-your-config)
- [Understand security constraints →](security.md)
