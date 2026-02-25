# CI/CD

The project uses two GitHub Actions workflows: one for continuous integration on every push, and one for the release pipeline triggered by version tags.

---

## CI Workflow (`ci.yml`)

**Triggers:** Push to `master`, pull requests to `master`

### Jobs

#### `build-and-test`

Runs on `ubuntu-latest`. Steps:

1. Checkout code
2. Set up .NET 8
3. `dotnet build`
4. `dotnet test`

This is the gate for every PR — it must pass before merging.

#### `template-smoke-test`

Depends on `build-and-test`. Verifies the `dotnet new mcp-tool` template works end-to-end:

1. Pack `McpServer.Plugin.Abstractions` to a local NuGet feed
2. Install the `mcp-tool` template from the built package
3. Scaffold a new plugin with `dotnet new mcp-tool -n SmokeTest`
4. Restore (using the local feed for the abstractions package)
5. Build — if this fails, the template is broken

---

## Release Workflow (`release.yml`)

**Trigger:** Tags matching `v*` (e.g. `v1.2.3`)

### Jobs

#### `build-binaries` (matrix)

Runs in parallel on `windows-latest`, `ubuntu-latest`, `macos-latest` for targets `win-x64`, `linux-x64`, `osx-arm64`.

Steps per platform:

1. Run all tests
2. `dotnet publish` with `--self-contained --single-file`
3. Upload artifact

#### `create-release`

Depends on `build-binaries`. Steps:

1. Download all three platform artifacts
2. Create a GitHub Release with the tag
3. Attach the three binaries as release assets

#### `publish-nuget`

Depends on `build-binaries`. Steps:

1. `dotnet pack` the main `McpServer` project
2. `dotnet nuget push` to NuGet.org

Requires the `NUGET_API_KEY` secret to be set in repository settings.

#### `publish-templates`

Depends on `publish-nuget`. Steps:

1. `dotnet pack` the `McpServer.Templates` project
2. `dotnet nuget push` to NuGet.org

---

## Creating a Release

```bash
git tag v1.2.3
git push origin v1.2.3
```

This triggers:

1. Build & test on all three platforms
2. GitHub Release with platform binaries attached
3. `DotnetMcpServer` pushed to NuGet.org
4. `DotnetMcpServer.Templates` pushed to NuGet.org

---

## Required Secrets

| Secret | Used by | Description |
|--------|---------|-------------|
| `NUGET_API_KEY` | `publish-nuget`, `publish-templates` | NuGet.org API key for pushing packages |

Set in: Repository Settings → Secrets and variables → Actions → New repository secret.
