# Dynamics 365 Business Central Admin MCP Server (YAMPI)

A Model Context Protocol (MCP) server that enables AI assistants like Claude Desktop, GitHub Copilot, and Cursor to manage Dynamics 365 Business Central environments through natural language commands.

## 🌟 Features

- 🔐 **Interactive Authentication** - Secure Microsoft Entra ID authentication with browser-based login
- 📦 **38 Administrative Tools** - Complete environment, app, session, PTE upload, and extension management
- ⚡ **Smart Token Caching** - Automatic token refresh to minimize authentication prompts
- 🌍 **Multi-Tenant Support** - Manage multiple Business Central tenants seamlessly
- 💬 **Natural Language Interface** - Control BC through conversational AI commands
- 🔒 **Secure by Design** - No credentials stored, uses Azure authentication standards
- 🛰️ **Admin Center API v2.29** - Built on the latest Business Central Administration Center API

## 📦 Installation

### Via npm (Recommended)

```bash
npm install -g @demiliani/d365bc-admin-mcp
```

### Prerequisites

- **Node.js 16+** - [Download](https://nodejs.org/)
- **Windows, Linux (glibc) or macOS, on x64 or ARM64** - npm installs only the self-contained server for your platform (about 15 MB, no .NET install needed); elsewhere, build from source
- **Dynamics 365 Business Central** - Admin access to your tenant
- **Microsoft Entra ID** - Azure account with BC admin privileges

## ⚙️ Configuration

After installation, configure your AI assistant to use the MCP server.

### Claude Desktop

**Location:**
- macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`
- Windows: `%APPDATA%\Claude\claude_desktop_config.json`
- Linux: `~/.config/Claude/claude_desktop_config.json`

**Configuration:**
```json
{
  "mcpServers": {
    "d365bc-admin": {
      "command": "d365bc-admin-mcp"
    }
  }
}
```

**Steps:**
1. Open the configuration file (create it if it doesn't exist)
2. Add the above configuration
3. Save the file
4. Restart Claude Desktop
5. You should see the MCP server connected in the chat window

### GitHub Copilot (VS Code)

**Location:** `.vscode/settings.json` in your workspace or global settings

**Configuration:**
```json
{
  "github.copilot.chat.mcp.servers": {
    "d365bc-admin": {
      "command": "d365bc-admin-mcp"
    }
  }
}
```

**Steps:**
1. Open VS Code Settings (JSON)
2. Add the above configuration
3. Save the file
4. Restart VS Code
5. Open GitHub Copilot Chat to start using the MCP server

### Cursor

**Location:** `.cursor/config.json` in your workspace

**Configuration:**
```json
{
  "mcp": {
    "servers": {
      "d365bc-admin": {
        "command": "d365bc-admin-mcp"
      }
    }
  }
}
```

**Steps:**
1. Create `.cursor/config.json` in your workspace root
2. Add the above configuration
3. Save the file
4. Restart Cursor
5. The MCP server will be available in Cursor's AI chat

## 🔐 Authentication

The server first tries the credentials already available on the machine (Azure CLI, Visual Studio, environment variables, managed identity). Otherwise, on first use, a browser window opens for Microsoft Entra ID authentication: sign in with your Business Central administrator account.

The sign-in is kept across restarts: refresh tokens are stored in the operating system's encrypted token cache (Windows DPAPI, macOS Keychain, Linux keyring) and the account is remembered in `%LOCALAPPDATA%/D365BCAdminMCP` (`~/Library/Application Support/D365BCAdminMCP` on macOS, `~/.local/share/D365BCAdminMCP` on Linux), so the browser opens again only when the refresh token expires. Where no encrypted store is available (e.g. Linux without a keyring), the login is kept in memory only. Set `D365BC_MCP_PERSIST_TOKEN_CACHE=false` to disable persistence; `clear_cached_token` signs out of a tenant.

**No additional setup required** - the MCP server handles authentication automatically!

## 💡 Usage Examples

Once configured, use natural language commands in your AI assistant:

### Environment Management
```
"Show me all my Business Central environments"
"What's the status of my Production environment?"
"Create a new sandbox environment called 'Dev-Test' in the US"
"Copy Production environment to a new sandbox called 'Testing'"
"Delete the 'Old-Test' sandbox environment"
"Rename the 'Testing' sandbox to 'UAT'"
"Which points in time can I restore Production to? Restore it to yesterday 08:00 UTC as a sandbox called 'Prod-Restore'"
"Show the running environment operations and how many environments I can still create"
"Delete the Production environment"   (you are asked to type 'Production' to confirm)
"Export the Production database to my storage account; how many exports are left this month?"
"Restrict Sandbox access to the 'BC Testers' security group"
```

### Application Management
```
"List all installed apps in Production"
"Show available updates for my apps"
"Install app with ID <app-id> in my Production environment"
"Update the Sales Module app to version 2.0"
"Uninstall the old Marketing app from Sandbox"
```

### Session Management
```
"Show active user sessions in Production"
"Terminate session abc123 in my Sandbox environment"
```

### Update Windows
```
"Get the update window settings for Production"
"Set update window for Production to 10 PM - 6 AM UTC"
```

### Storage & Monitoring
```
"Show storage usage for all environments"
"What's the storage usage for Production?"
```

### Extension Deployment & PTE Uploads
```
"Install c:\\Packages\\MyPTE.app on Production (Fabrikam) during the update window"
"Install all .app files from c:\\Packages into Sandbox now, in dependency order, installing any missing AppSource apps"
"Show the scheduled PTE installs for Production and cancel the one for MyPTE 2.0.0.0"
"Check the extension deployment status for Fabrikam in Production"
```

## 🛠️ Available Tools (51 Total)

The MCP server exposes 51 administrative tools organized by category:

### 🔑 Authentication & Tenant Management (2 tools)

| Tool | Description |
|------|-------------|
| `get_microsoft_entra_id_token` | Sign in to a tenant and cache the token used by the other tools (returns status and expiry, never the token) |
| `get_tenant_id_from_tenant_name` | Retrieve tenant ID from tenant name |

### 🌍 Environment Management (16 tools)

| Tool | Description |
|------|-------------|
| `get_environment_informations` | List all BC environments in a tenant |
| `create_new_environment` | Create a new BC environment (optionally on a specific ring and application version) |
| `copy_environment` | Copy an existing environment to create a new one |
| `delete_environment` | Delete a Business Central environment |
| `recover_environment` | Recover a recently deleted environment |
| `rename_environment` | Rename an environment (its URL changes, so integrations must be updated) |
| `restore_environment` | Restore an environment to a point in time as a new environment |
| `get_available_restore_periods` | List the time ranges an environment can be restored to |
| `get_environment_operations` | List environment operations (create, copy, rename, restore, ...) for the tenant or one environment |
| `get_environment_quotas` | Get the environment and storage quotas of the tenant |
| `get_environment_update_window` | Get update window settings for an environment |
| `set_environment_update_window` | Configure update window for an environment |
| `set_environment_application_insights_connection_string` | Set Application Insights connection string |
| `get_usage_storage_for_environment` | Get storage usage for a specific environment |
| `get_usage_storage_for_all_environments` | Get storage usage for all environments |
| `get_environment_companies` | List companies in an environment |

### 📦 Application Management (6 tools)

| Tool | Description |
|------|-------------|
| `get_installed_apps` | List apps installed in an environment |
| `get_available_app_updates` | Check for available app updates |
| `install_app` | Install a Marketplace (AppSource) app in a Business Central environment |
| `update_app` | Update an app to a specific version |
| `uninstall_app` | Remove an app from an environment |
| `get_app_operations` | Get status of app install/update/uninstall operations |

### 🔄 Environment Updates (2 tools)

| Tool | Description |
|------|-------------|
| `get_environment_updates` | Check for available BC version updates |
| `set_environment_update` | Schedule or run an environment update |

### 👥 Session Management (2 tools)

| Tool | Description |
|------|-------------|
| `get_active_sessions` | List active user sessions in an environment |
| `kill_active_sessions` | Terminate a specific active session |

### 🔔 Notification Management (3 tools)

| Tool | Description |
|------|-------------|
| `get_environment_notification_recipient` | List notification recipients |
| `create_environment_notification_recipient` | Add a new notification recipient |
| `delete_environment_notification_recipient` | Remove a notification recipient |

### 🔐 Environment Access (5 tools)

| Tool | Description |
|------|-------------|
| `get_environment_security_group` | Get the Microsoft Entra security group that restricts access to an environment |
| `set_environment_security_group` | Restrict access to the members of a security group |
| `clear_environment_security_group` | Remove the security group restriction |
| `get_access_with_m365_licenses` | Check whether users can access the environment with Microsoft 365 licenses |
| `set_access_with_m365_licenses` | Allow or disallow access with Microsoft 365 licenses |

### 💾 Database Export (2 tools)

| Tool | Description |
|------|-------------|
| `get_database_export_metrics` | Exports allowed per month and remaining this month |
| `start_database_export` | Export the environment database as a .bacpac file to an Azure Storage account |

`start_database_export` needs a paid subscription, the D365 BACKUP/RESTORE permission set and a Blob service SAS URI of an Azure **standard** storage account with Read, Write, Create and Delete permissions. The SAS URI is never logged or returned by the server, but it is part of the conversation with the AI assistant, so use a short-lived one. Track the export with `get_environment_operations`.

### 🔧 Feature Management (3 tools)

| Tool | Description |
|------|-------------|
| `get_available_features` | List available features in an environment |
| `activate_feature` | Activate a feature in a Business Central environment |
| `deactivate_feature` | Deactivate a feature in a Business Central environment |

### 🚀 PTE Management via Admin Center API v2.29 (4 tools)

| Tool | Description |
|------|-------------|
| `install_pte` | Upload a PTE (.app, max 50 MB) and install it immediately or on a schedule |
| `install_ptes_from_folder` | Upload all .app files in a folder in dependency order, waiting for each immediate install |
| `get_scheduled_pte_operations` | List scheduled PTE installs and updates |
| `cancel_scheduled_pte` | Cancel a scheduled PTE install or update |

`deploymentSchedule` accepts `Immediate`, `UpdateWindow`, `NextMinorUpdate` and `NextMajorUpdate` (new PTEs only accept `Immediate` or `UpdateWindow`); `syncMode` accepts `Add` and `ForceSync`.

`install_ptes_from_folder` reads each package's manifest (`app.json`, or `NavxManifest.xml` generated by the compiler) and uploads an app only after the apps it depends on. All files are validated (size, readable manifest, duplicates, circular dependencies) before anything is uploaded; if an app fails, the apps depending on it are skipped.

**Dependencies outside the folder** (for example AppSource apps) are checked against the apps installed in the environment before anything is uploaded. With `installMissingDependencies` (default `true`), missing apps are installed and apps older than the declared minimum version are updated, waiting for each operation; they run immediately, also when the PTEs are scheduled, and go to the latest version available for the environment. If a dependency cannot be installed, only the PTEs depending on it are skipped. With `installMissingDependencies = false` the tool stops and lists what is missing. Microsoft platform apps (System Application, Base Application, ...) are never installed or updated: if the environment version is too old, an environment update is required.

These tools require an Admin Center role (Dynamics 365 Business Central Administrator, Dynamics 365 Administrator, or delegated admin).

### 🧩 Extension Deployment & PTE Uploads – Automation API (4 tools, deprecated)

Uploading PTEs through the Automation API is being removed in 2027 release wave 1 (version 30.0). Use the PTE Management tools above instead.

| Tool | Description |
|------|-------------|
| `get_extension_deployment_status` | Get extension deployment status for an environment/company |
| `create_pte_upload` | Create and upload a single PTE (.app) file |
| `create_pte_uploads_from_folder` | Bulk upload PTEs from a folder, waiting for each deployment |
| `get_extension_upload` | Retrieve the status of a specific extension upload task |

### 🧹 Token Management (2 tools)

| Tool | Description |
|------|-------------|
| `clear_cached_token` | Clear cached authentication token for a tenant |
| `get_token_cache_status` | View cached token status for all tenants |

### Tool behaviour

- **Annotations:** every tool declares MCP hints, so clients can run read-only tools (`get_*`) without confirmation. Tools that delete or break things (`delete_environment`, `rename_environment`, `uninstall_app`, `kill_active_sessions`, `delete_environment_notification_recipient`, `cancel_scheduled_pte`) are marked destructive, as are `set_environment_security_group` and `clear_environment_security_group`.
- **Confirmation for production:** deleting or renaming a **Production** environment, and uninstalling an app with `deleteData` in production, require the user to type the environment name. Clients that support MCP elicitation (confirmation prompts) show a prompt and a name supplied by the model is ignored; with other clients the assistant must ask the user and pass `confirmEnvironmentName`. Sandboxes are not affected.
- **Throttling and transient errors:** calls answered with 429 or 503 are retried up to 3 times, honouring `Retry-After` (max 60 s). Read calls are also retried on 502, 504, network errors and timeouts; write calls are not, to avoid running an operation twice. Each HTTP request times out after 5 minutes.
- **Cancellation and progress:** cancelling a request in the client stops the tool, including the waits of `install_pte` and `install_ptes_from_folder`, which also report progress. An operation already accepted by Business Central keeps running server-side; check it with `get_app_operations` or `get_environment_operations`.

## 🎯 Quick Start

1. **Install the package:**
   ```bash
   npm install -g @demiliani/d365bc-admin-mcp
   ```

   > 📖 **Need detailed installation instructions?** See the [Installation Guide](INSTALLATION.md) for platform-specific steps (Windows, macOS, Linux) and troubleshooting.

2. **Configure your AI assistant** (see Configuration section above)

3. **Start using it:**
   - Open your AI assistant (Claude, Copilot, or Cursor)
   - Type: "Show me all my Business Central environments"
   - Authenticate when prompted (first time only)
   - Start managing your BC environments with natural language!

## 📚 Documentation

For detailed installation documentation, see:
- [Installation Guide](INSTALLATION.md) - Detailed installation steps

## 🔍 Troubleshooting

### "Command not found: d365bc-admin-mcp"
```bash
npm install -g @demiliani/d365bc-admin-mcp
which d365bc-admin-mcp
```

### Authentication Issues
- Ensure you have Business Central admin privileges
- Check that you're signing in with the correct Microsoft account
- Try clearing the token cache: "Clear cached token for tenant [tenant-id]"

### MCP Server Not Appearing
- Verify the config file location for your OS
- Check for JSON syntax errors in the config
- Restart your AI assistant completely
- Check the assistant's logs for error messages

### Diagnostic Logging
The server writes its logs to **stderr** only (stdout is reserved for the MCP protocol). Set the `D365BC_MCP_LOG_LEVEL` environment variable to `Debug`, `Information` (default), `Warning`, `Error` or `None`. Access tokens and response bodies are never logged.

When an Admin Center API call fails, tools return the API error instead of a generic message, e.g. `{"error": "HTTP 404 Not Found: EnvironmentNotFound: ..."}`.

## 🤝 Contributing

Contributions are welcome! Please feel free to submit issues, feature requests, or pull requests.

Building from source requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned in `global.json`). Build and run the tests (the same steps run in CI on every pull request):

```bash
dotnet build src/D365BCAdminMCP.sln
dotnet test src/D365BCAdminMCP.sln
```

`npm run build` publishes the native server for your platform into `build/`, and `npm test` starts it through the npm launcher and checks the MCP handshake and the tool list.

### Releasing

Releases are published by [`.github/workflows/release.yml`](.github/workflows/release.yml) with [npm trusted publishing](https://docs.npmjs.com/trusted-publishers): GitHub Actions authenticates to npm with a short-lived OIDC token, so no npm token is stored anywhere, and every package gets a provenance attestation. The workflow only *stages* the packages; they go live when a maintainer approves them with 2FA.

Each release is seven npm packages: `@demiliani/d365bc-admin-mcp` (the launcher) and one package per platform with its server binary (`@demiliani/d365bc-admin-mcp-win-x64`, `-win-arm64`, `-linux-x64`, `-linux-arm64`, `-osx-x64`, `-osx-arm64`). The main package lists them as optional dependencies pinned to its own version, and npm installs only the one matching the user's OS and CPU.

To release:

1. Bump the version in `package.json` (the csproj reads it from there), e.g. `npm version 1.4.0 --no-git-tag-version`, and merge it to `main`.
2. Tag the merge commit and push the tag: `git tag v1.4.0 && git push origin v1.4.0`. The workflow refuses tags that don't match `package.json` or that point outside `main`. A tag such as `v1.4.0-beta.1` publishes under the `next` dist-tag.
3. The workflow runs the tests, builds and smoke-tests each binary on its own OS and CPU, installs the packed packages like a user would, and stages them on npm.
4. On npmjs.com (each package → **Staged Packages**) approve the six platform packages first and `@demiliani/d365bc-admin-mcp` last, with 2FA. Approving the main package first would briefly leave users without a binary.
5. Review and publish the draft GitHub release, which carries the binaries, their SHA-256 checksums and build provenance attestations.

One-time setup:

- **GitHub** → repository **Settings** → **Environments** → create `npm-publish`, and under **Deployment branches and tags** allow only tags matching `v*`. Optionally add required reviewers.
- **GitHub** → **Settings** → **Rules** → add a tag ruleset for `v*` so only maintainers can create, update or delete release tags.
- **GitHub** → **Settings** → **Code security** → enable **Private vulnerability reporting** (used by [SECURITY.md](SECURITY.md)).
- **npmjs.com**, for each of the seven packages → **Settings** → **Trusted Publisher** → **GitHub Actions**: organization or user `demiliani`, repository `D365BCAdminMCP`, workflow filename `release.yml`, environment `npm-publish`, allowed action **stage publish**. npm expects the first publish through a new trusted publisher within 2 days of creating it.
- After the first release goes through, set **Publishing access** to *Require two-factor authentication and disallow tokens* on every package, and revoke any old automation tokens.

First release with platform packages: npm only lets you configure a trusted publisher on a package that already exists, so the six platform packages are published by hand once:

1. Configure the trusted publisher on `@demiliani/d365bc-admin-mcp` and push the release tag. The **Stage npm packages** job fails on the first platform package, which doesn't exist yet.
2. Download the `npm-packages` artifact from that workflow run, then from its folder run `npm login` and `for f in demiliani-d365bc-admin-mcp-*-<version>.tgz; do npm publish "$f"; done`, or in PowerShell `Get-ChildItem demiliani-d365bc-admin-mcp-*-<version>.tgz | ForEach-Object { npm publish $_.FullName }`. The pattern matches the six platform packages, not the main one.
3. Configure the trusted publisher on each of the six platform packages.
4. Re-run the failed jobs: versions already on npm are skipped, so only the main package is staged. Approve it.

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 📮 Support

- **npm Package:** https://www.npmjs.com/package/@demiliani/d365bc-admin-mcp
- **Issues:** Report bugs or request features via GitHub Issues
- **Version:** 1.2.0

## 🙏 Acknowledgments

Built with:
- [Model Context Protocol](https://modelcontextprotocol.io/) - Standard for AI assistant integrations
- [Azure Identity](https://learn.microsoft.com/azure/developer/intro/azure-developer-identity) - Microsoft authentication
- [Business Central Admin API](https://learn.microsoft.com/dynamics365/business-central/dev-itpro/administration/administration-center-api) - BC administration

---

Made with ❤️ for the Dynamics 365 Business Central community
