# Installation Guide - D365 Business Central Admin MCP Server

Complete installation instructions for Windows, macOS, and Linux.

## 📋 Prerequisites

Before installing, ensure you have:

- **Node.js 16.0 or higher** - [Download from nodejs.org](https://nodejs.org/)
- **npm** (comes with Node.js)
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

GitHub Copilot in VS Code supports MCP servers through settings.

**Configuration Steps:**

1. **Open VS Code Settings:**
   - Press `Ctrl+,` (Windows/Linux) or `Cmd+,` (macOS)
   - Click the "Open Settings (JSON)" icon in the top-right corner

2. **Add the MCP configuration:**

   ```json
   {
     "github.copilot.chat.mcp.servers": {
       "d365bc-admin": {
         "command": "d365bc-admin-mcp"
       }
     }
   }
   ```

3. **Alternative: Workspace Settings**

   Create or edit `.vscode/settings.json` in your workspace:

   ```json
   {
     "github.copilot.chat.mcp.servers": {
       "d365bc-admin": {
         "command": "d365bc-admin-mcp"
       }
     }
   }
   ```

4. **Reload VS Code** (`Ctrl+Shift+P` > "Developer: Reload Window")

5. **Verify:** Open GitHub Copilot Chat and try: "List my BC environments"

---

### Cursor Configuration

Cursor uses workspace-specific configuration files for MCP servers.

**Configuration Steps:**

1. **Create the config directory in your workspace:**

   **Windows (Command Prompt):**
   ```cmd
   mkdir .cursor
   notepad .cursor\config.json
   ```

   **macOS/Linux (Terminal):**
   ```bash
   mkdir -p .cursor
   nano .cursor/config.json
   ```

2. **Add the following configuration:**

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

3. **Save the file** and restart Cursor

4. **Verify:** Open Cursor's AI chat and test the MCP server

---

## 🔐 Authentication Setup

The MCP server uses **interactive browser authentication** through Microsoft Entra ID.

**First-Time Setup:**

1. After configuring your AI assistant, try a command like:
   ```
   "Show me all my Business Central environments"
   ```

2. A browser window will automatically open

3. Sign in with your **Microsoft account** that has Business Central admin privileges

4. Grant the requested permissions

5. The token is cached for **50 minutes** and automatically refreshed

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

### .NET SDK Not Found During Installation

The MCP server requires .NET SDK 9.0 for building. If you see this error:

**Windows:**
```powershell
# Install via winget
winget install Microsoft.DotNet.SDK.9

# Or download from https://dotnet.microsoft.com/download
```

**macOS:**
```bash
brew install --cask dotnet-sdk
```

**Linux:**
```bash
# Ubuntu/Debian
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 9.0

# Or follow: https://learn.microsoft.com/dotnet/core/install/linux
```

### MCP Server Not Appearing in AI Assistant

1. **Verify config file location** - Check platform-specific paths above
2. **Check JSON syntax** - Use a JSON validator
3. **Restart the AI assistant completely**
4. **Check logs:**
   - **Claude Desktop:** Look for logs in the app's console
   - **VS Code:** Open Output panel > GitHub Copilot Chat
   - **Cursor:** Check the developer console

### Authentication Issues

- **Ensure you have BC admin privileges** in your tenant
- **Use the correct Microsoft account** associated with Business Central
- **Check your firewall** - Allow browser authentication
- **Clear cached token:** Tell the AI assistant: "Clear cached token for tenant [your-tenant-id]"

---

## 🔄 Updating

To update to the latest version:

```bash
npm update -g @demiliani/d365bc-admin-mcp
```

Check the current version:

```bash
npm list -g @demiliani/d365bc-admin-mcp
```

---

## 🗑️ Uninstallation

To remove the MCP server:

```bash
npm uninstall -g @demiliani/d365bc-admin-mcp
```

Don't forget to remove the MCP server configuration from your AI assistant's config files.

---

## 📚 Next Steps

- **[Quick Start Guide](docs/QUICKSTART.md)** - Get started in 5 minutes
- **[README](README.md)** - Full documentation and usage examples
- **[Publishing Guide](docs/PUBLISHING.md)** - For contributors

---

## 📮 Support

- **npm Package:** https://www.npmjs.com/package/@demiliani/d365bc-admin-mcp
- **Issues:** Report problems via GitHub Issues
- **Version:** 1.0.0