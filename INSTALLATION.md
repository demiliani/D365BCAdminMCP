# Installation Guide - D365 Business Central Admin MCP Server (YAMPI)

Complete installation instructions for Windows, macOS, and Linux.

## 📋 Prerequisites

Before installing, ensure you have:

- **Node.js 18 or higher** - [Download from nodejs.org](https://nodejs.org/)
- **npm** (comes with Node.js)
- **Windows, Linux (glibc) or macOS, on x64 or ARM64** - npm installs a self-contained server for your platform, so no .NET runtime or SDK is needed
- **Dynamics 365 Business Central** - Admin access to your tenant
- **Microsoft Entra ID** - Azure account with Business Central admin privileges

### Verify Prerequisites

Check if Node.js and npm are installed:

```bash
node --version
npm --version
```

If not installed, follow the platform-specific instructions below.

---

## 🪟 Windows Installation

### Step 1: Install Node.js

1. Download the Windows installer from [nodejs.org](https://nodejs.org/)
2. Run the installer (`.msi` file)
3. Follow the installation wizard
4. Restart your terminal/command prompt

### Step 2: Install the MCP Server

Open **Command Prompt** or **PowerShell** and run:

```powershell
npm install -g @demiliani/d365bc-admin-mcp
```

### Step 3: Verify Installation

```powershell
d365bc-admin-mcp --version
```

You should see the version number if the installation was successful.

---

## 🍎 macOS Installation

### Step 1: Install Node.js

**Option A: Using Homebrew (Recommended)**

```bash
# Install Homebrew if not already installed
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"

# Install Node.js
brew install node
```

**Option B: Direct Download**

1. Download the macOS installer from [nodejs.org](https://nodejs.org/)
2. Run the `.pkg` installer
3. Follow the installation wizard

### Step 2: Install the MCP Server

Open **Terminal** and run:

```bash
npm install -g @demiliani/d365bc-admin-mcp
```

### Step 3: Verify Installation

```bash
d365bc-admin-mcp --version
which d365bc-admin-mcp
```

---

## 🐧 Linux Installation

### Step 1: Install Node.js

**Ubuntu/Debian:**

```bash
# Update package index
sudo apt update

# Install Node.js and npm
sudo apt install nodejs npm

# Or install the latest LTS version using NodeSource
curl -fsSL https://deb.nodesource.com/setup_lts.x | sudo -E bash -
sudo apt-get install -y nodejs
```

**Fedora/RHEL/CentOS:**

```bash
# Install Node.js from NodeSource
curl -fsSL https://rpm.nodesource.com/setup_lts.x | sudo bash -
sudo dnf install -y nodejs
```

**Arch Linux:**

```bash
sudo pacman -S nodejs npm
```

### Step 2: Install the MCP Server

```bash
npm install -g @demiliani/d365bc-admin-mcp
```

### Step 3: Verify Installation

```bash
d365bc-admin-mcp --version
which d365bc-admin-mcp
```

---

## 🔧 MCP Configuration

After installation, configure your AI assistant to use the MCP server.

### Claude Desktop Configuration

Claude Desktop stores its configuration in a JSON file. The location varies by operating system:

**File Locations:**

| Platform | Configuration File Path |
|----------|------------------------|
| **Windows** | `%APPDATA%\Claude\claude_desktop_config.json` |
| **macOS** | `~/Library/Application Support/Claude/claude_desktop_config.json` |
| **Linux** | `~/.config/Claude/claude_desktop_config.json` |

**Configuration Steps:**

1. **Locate or create the config file:**

   **Windows (PowerShell):**
   ```powershell
   notepad "$env:APPDATA\Claude\claude_desktop_config.json"
   ```

   **macOS/Linux (Terminal):**
   ```bash
   # macOS
   mkdir -p ~/Library/Application\ Support/Claude
   nano ~/Library/Application\ Support/Claude/claude_desktop_config.json

   # Linux
   mkdir -p ~/.config/Claude
   nano ~/.config/Claude/claude_desktop_config.json
   ```

2. **Add the following configuration:**

   ```json
   {
     "mcpServers": {
       "d365bc-admin": {
         "command": "d365bc-admin-mcp"
       }
     }
   }
   ```

3. **Save the file** and restart Claude Desktop

4. **Verify:** Look for the MCP server connection indicator in Claude Desktop

---

### GitHub Copilot (VS Code) Configuration

VS Code reads MCP servers from `mcp.json` files. The quickest way is the **Install in VS Code** button in the [README](README.md#github-copilot-vs-code), which runs the server through `npx`.

**Configuration Steps:**

1. **Open the configuration:** press `Ctrl+Shift+P` (`Cmd+Shift+P` on macOS) and run **MCP: Open User Configuration** (all workspaces), or create `.vscode/mcp.json` in a workspace.

2. **Add the server:**

   ```json
   {
     "servers": {
       "d365bc-admin": {
         "type": "stdio",
         "command": "d365bc-admin-mcp"
       }
     }
   }
   ```

3. **Start it:** click **Start** above the server entry in `mcp.json`, or run **MCP: List Servers** → `d365bc-admin` → **Start Server**.

4. **Verify:** open Copilot Chat in **Agent** mode and try: "List my BC environments"

---

### Cursor Configuration

Cursor reads MCP servers from `~/.cursor/mcp.json` (all projects) or `.cursor/mcp.json` (one project).

**Configuration Steps:**

1. **Open or create the file**, for example for all projects:

   **Windows (PowerShell):**
   ```powershell
   New-Item -ItemType Directory -Force "$HOME\.cursor" | Out-Null
   notepad "$HOME\.cursor\mcp.json"
   ```

   **macOS/Linux (Terminal):**
   ```bash
   mkdir -p ~/.cursor
   nano ~/.cursor/mcp.json
   ```

2. **Add the following configuration:**

   ```json
   {
     "mcpServers": {
       "d365bc-admin": {
         "command": "d365bc-admin-mcp"
       }
     }
   }
   ```

3. **Save the file**, then check **Cursor Settings → MCP**: the server should be listed and enabled

4. **Verify:** Open Cursor's AI chat (Agent) and test the MCP server

---

## 🔐 Authentication Setup

The MCP server authenticates with Microsoft Entra ID. It first uses credentials already available on the machine (Azure CLI, Visual Studio, environment variables, managed identity); otherwise it opens an **interactive browser login**.

**First-Time Setup:**

1. After configuring your AI assistant, try a command like:
   ```
   "Show me all my Business Central environments"
   ```

2. A browser window opens (unless you are already signed in with the Azure CLI or Visual Studio)

3. Sign in with your **Microsoft account** that has Business Central admin privileges

4. Grant the requested permissions

5. The sign-in is remembered across restarts through the operating system's encrypted token cache, so the browser opens again only when the refresh token expires (see [Authentication](README.md#-authentication))

**No additional configuration needed!** The MCP server handles authentication automatically.

---

## ✅ Verification

Test your installation with these steps:

1. **Check the command is available:**

   **Windows:**
   ```powershell
   where d365bc-admin-mcp
   ```

   **macOS/Linux:**
   ```bash
   which d365bc-admin-mcp
   ```

2. **Check the version:**
   ```bash
   d365bc-admin-mcp --version
   ```

3. **Test in your AI assistant:**
   - Open Claude Desktop, GitHub Copilot Chat, or Cursor
   - Type: "List all my Business Central environments"
   - Authenticate when prompted
   - You should see your environments listed

---

## 🔍 Troubleshooting

### "Command not found: d365bc-admin-mcp"

**Solution:**

1. Verify npm global packages location:
   ```bash
   npm config get prefix
   ```

2. Ensure the npm bin directory is in your PATH:

   **Windows:** Add `%APPDATA%\npm` to your PATH
   
   **macOS/Linux:** Add `~/.npm-global/bin` or `/usr/local/bin` to your PATH

3. Reinstall:
   ```bash
   npm uninstall -g @demiliani/d365bc-admin-mcp
   npm install -g @demiliani/d365bc-admin-mcp
   ```

### Permission Errors (macOS/Linux)

If you get `EACCES` permission errors:

**Solution 1: Fix npm permissions (Recommended)**
```bash
mkdir ~/.npm-global
npm config set prefix '~/.npm-global'
echo 'export PATH=~/.npm-global/bin:$PATH' >> ~/.bashrc
source ~/.bashrc
npm install -g @demiliani/d365bc-admin-mcp
```

**Solution 2: Use sudo (Not Recommended)**
```bash
sudo npm install -g @demiliani/d365bc-admin-mcp
```

### "The package @demiliani/d365bc-admin-mcp-… is not installed"

The server binary comes from an optional dependency matching your OS and CPU (e.g. `@demiliani/d365bc-admin-mcp-win-x64`). npm skips it when optional dependencies are omitted, so reinstall without `--omit=optional` / `--no-optional`:

```bash
npm install -g @demiliani/d365bc-admin-mcp
```

Platforms other than Windows, Linux (glibc) and macOS on x64/ARM64 have no prebuilt binary: build from source with the .NET 10 SDK (see [Contributing](README.md#-contributing)).

### Upgrade Fails with EPERM / EBUSY (Windows)

A running MCP server keeps its executable locked. Close VS Code, Claude Desktop or Cursor (or stop the MCP server) and run the install again.

### MCP Server Not Appearing in AI Assistant

1. **Verify config file location** - Check platform-specific paths above
2. **Check JSON syntax** - Use a JSON validator
3. **Restart the AI assistant completely**
4. **Check logs:**
   - **Claude Desktop:** Look for logs in the app's console
   - **VS Code:** run **MCP: List Servers** → `d365bc-admin` → **Show Output**
   - **Cursor:** **Cursor Settings → MCP** shows the server status and errors
5. **More detail:** set the environment variable `D365BC_MCP_LOG_LEVEL` to `Debug` in the server configuration (logs go to stderr)

### Authentication Issues

- **Ensure you have BC admin privileges** in your tenant
- **Use the correct Microsoft account** associated with Business Central
- **Check your firewall** - Allow browser authentication
- **Clear cached token:** Tell the AI assistant: "Clear cached token for tenant [your-tenant-id]"
- **No login window, or wrong account:** Tell the AI assistant: "Force interactive login for tenant [your-tenant-id]" (`force_interactive_auth` opens the browser and lets you choose the account)

---

## 🔄 Updating

Close your AI assistant (or stop the MCP server) first, then install the latest version:

```bash
npm install -g @demiliani/d365bc-admin-mcp@latest
```

Check the current version:

```bash
d365bc-admin-mcp --version
```

See the [release notes](https://github.com/demiliani/D365BCAdminMCP/releases) for what changed.

---

## 🗑️ Uninstallation

To remove the MCP server:

```bash
npm uninstall -g @demiliani/d365bc-admin-mcp
```

Don't forget to remove the MCP server configuration from your AI assistant's config files.

---

## 📚 Next Steps

- **[Quick Start](README.md#-quick-start)** - Get started in 5 minutes
- **[README](README.md)** - Full documentation, tools and usage examples
- **[Contributing and releasing](README.md#-contributing)** - For contributors

---

## 📮 Support

- **npm Package:** https://www.npmjs.com/package/@demiliani/d365bc-admin-mcp
- **Issues:** Report problems via [GitHub Issues](https://github.com/demiliani/D365BCAdminMCP/issues)