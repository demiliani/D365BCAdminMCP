# Dynamics 365 Business Central Admin MCP Server

A Model Context Protocol (MCP) server that enables AI assistants like Claude Desktop, GitHub Copilot, and Cursor to manage Dynamics 365 Business Central environments through natural language commands.

## 🌟 Features

- 🔐 **Interactive Authentication** - Secure Microsoft Entra ID authentication with browser-based login
- 📦 **28 Administrative Tools** - Complete environment, app, and session management
- ⚡ **Smart Token Caching** - Automatic token refresh to minimize authentication prompts
- 🌍 **Multi-Tenant Support** - Manage multiple Business Central tenants seamlessly
- 💬 **Natural Language Interface** - Control BC through conversational AI commands
- 🔒 **Secure by Design** - No credentials stored, uses Azure authentication standards

## 📦 Installation

### Via npm (Recommended)

```bash
npm install -g @demiliani/d365bc-admin-mcp
```

### Prerequisites

- **Node.js 16+** - [Download](https://nodejs.org/)
- **Dynamics 365 Business Central** - Admin access to your tenant
- **Microsoft Entra ID** - Azure account with BC admin privileges

## �� Configuration

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

On first use, a browser window will open for Microsoft Entra ID authentication. Sign in with your Business Central administrator account. The token is cached for 50 minutes and automatically refreshed.

**No additional setup required** - the MCP server handles authentication automatically!

## 💡 Usage Examples

Once configured, use natural language commands in your AI assistant:

### Environment Management
```
"Show me all my Business Central environments"
"What's the status of my Production environment?"
"Create a new sandbox environment called 'Dev-Test' in the US"
"Copy Production environment to a new sandbox called 'Testing'"
```

### Application Management
```
"List all installed apps in Production"
"Show available updates for my apps"
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

## 🛠️ Available Tools (28 Total)

The MCP server exposes 28 administrative tools organized by category:

### 🔑 Authentication & Tenant Management (3 tools)

| Tool | Description |
|------|-------------|
| `get_microsoft_entra_id_token` | Get authentication token with intelligent caching |
| `get_tenant_id_from_tenant_name` | Retrieve tenant ID from tenant name |
| `get_token_cache_status` | View cached token status for all tenants |

### 🌍 Environment Management (9 tools)

| Tool | Description |
|------|-------------|
| `get_environment_informations` | List all BC environments in a tenant |
| `create_new_environment` | Create a new BC environment |
| `copy_environment` | Copy an existing environment to create a new one |
| `get_environment_update_window` | Get update window settings for an environment |
| `set_environment_update_window` | Configure update window for an environment |
| `set_app_insights_key` | Set Application Insights connection string |
| `get_environment_storage_usage` | Get storage usage for a specific environment |
| `get_all_environments_storage_usage` | Get storage usage for all environments |
| `get_companies` | List companies in an environment |

### 📦 Application Management (5 tools)

| Tool | Description |
|------|-------------|
| `get_installed_apps` | List apps installed in an environment |
| `get_available_app_updates` | Check for available app updates |
| `update_app` | Update an app to a specific version |
| `uninstall_app` | Remove an app from an environment |
| `get_app_operations` | Get status of app install/update/uninstall operations |

### 🔄 Environment Updates (2 tools)

| Tool | Description |
|------|-------------|
| `get_available_environment_updates` | Check for available BC version updates |
| `schedule_environment_update` | Schedule or run an environment update |

### 👥 Session Management (2 tools)

| Tool | Description |
|------|-------------|
| `get_active_sessions` | List active user sessions in an environment |
| `kill_active_sessions` | Terminate a specific active session |

### 🔔 Notification Management (3 tools)

| Tool | Description |
|------|-------------|
| `get_notification_recipients` | List notification recipients |
| `create_notification_recipient` | Add a new notification recipient |
| `delete_notification_recipient` | Remove a notification recipient |

### 🔧 Feature Management (3 tools)

| Tool | Description |
|------|-------------|
| `get_available_features` | List available features in an environment |
| `activate_feature` | Activate a feature in a Business Central environment |
| `deactivate_feature` | Deactivate a feature in a Business Central environment |

### 🧹 Token Management (1 tool)

| Tool | Description |
|------|-------------|
| `clear_cached_token` | Clear cached authentication token for a tenant |

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

## 🤝 Contributing

Contributions are welcome! Please feel free to submit issues, feature requests, or pull requests.

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 📮 Support

- **npm Package:** https://www.npmjs.com/package/@demiliani/d365bc-admin-mcp
- **Issues:** Report bugs or request features via GitHub Issues
- **Version:** 1.0.0

## 🙏 Acknowledgments

Built with:
- [Model Context Protocol](https://modelcontextprotocol.io/) - Standard for AI assistant integrations
- [Azure Identity](https://learn.microsoft.com/azure/developer/intro/azure-developer-identity) - Microsoft authentication
- [Business Central Admin API](https://learn.microsoft.com/dynamics365/business-central/dev-itpro/administration/administration-center-api) - BC administration

---

Made with ❤️ for the Dynamics 365 Business Central community
