# Copilot Instructions for D365BCAdminMCP

## Project Overview
- This is a Model Context Protocol (MCP) server for managing Microsoft Dynamics 365 Business Central environments via natural language, designed for integration with AI assistants (GitHub Copilot, Claude, Cursor, etc.).
- Written in C# (.NET 9), the main logic is in `src/D365BCAdminMCP/`.
- The server exposes 33+ administrative tools for environments, apps, sessions, PTE uploads, and extension management.
- Authentication uses Microsoft Entra ID (Azure AD) with browser-based login and smart token caching (no credentials stored).

## Key Architecture & Patterns
- **Entry Point:** `src/D365BCAdminMCP/Program.cs` initializes the MCP server and handles command routing.
- **Service Layer:** Core logic is in `D365BCAdminService.cs` and `D365BCAdminModels.cs`.
- **Models:** All Business Central entities and operations are defined in `src/D365BCAdminMCP/Models/`.
- **Configuration:** No hardcoded secrets; all authentication is interactive and secure.
- **Multi-Tenant:** Designed to manage multiple BC tenants in parallel.
- **Natural Language:** All commands are mapped from natural language to MCP operations.

## Developer Workflows
- **Build:**
  - Use `npm install -g @demiliani/d365bc-admin-mcp` for global CLI install.
  - For local dev: `dotnet build src/D365BCAdminMCP/D365BCAdminMCP.csproj -c Release -r linux-x64` (see `scripts/build.sh`).
- **Run:**
  - Start with `d365bc-admin-mcp` (after build/install).
- **Authentication:**
  - On first run, browser-based Microsoft login is required. Token is cached for 50 minutes.
  - When automatic login fails, run `clear_cached_token` followed by `force_interactive_auth` to relaunch the Microsoft Entra browser prompt for a tenant.
- **Configuration:**
  - For Copilot: add the MCP server config to `.vscode/settings.json` as shown in the README.
- **Troubleshooting:**
  - See `README.md` and `INSTALLATION.md` for platform-specific issues and solutions.

## Conventions & Integration
- **No credentials in code/config.**
- **All AI assistants use the same MCP server interface.**
- **Error handling:** Follows .NET conventions; errors are surfaced to the AI assistant for user feedback.
- **External dependencies:** Azure Identity, Business Central Admin API, Model Context Protocol.
- **Key files:**
  - `src/D365BCAdminMCP/Program.cs` (entry)
  - `src/D365BCAdminMCP/D365BCAdminService.cs` (core logic)
  - `src/D365BCAdminMCP/Models/` (data models)
  - `scripts/build.sh` (build automation)
  - `README.md`, `INSTALLATION.md` (usage, troubleshooting)

## Example Natural Language Commands
- "Show me all my Business Central environments"
- "Create a new sandbox environment called 'Dev-Test' in the US"
- "Delete the 'Old-Test' sandbox environment"

## References
- [README.md](../README.md) for full usage, config, and troubleshooting
- [INSTALLATION.md](../INSTALLATION.md) for platform-specific setup
- [Model Context Protocol](https://modelcontextprotocol.io/)

---
For new patterns or changes, update this file to keep AI agents productive and aligned with project conventions.
