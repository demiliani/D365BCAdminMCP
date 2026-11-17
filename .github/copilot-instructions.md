# Copilot Instructions for D365BCAdminMCP

## Project Overview
- This is a Model Context Protocol (MCP) server for managing Microsoft Dynamics 365 Business Central environments via natural language, designed for integration with AI assistants (GitHub Copilot, Claude, Cursor, etc.).
- Written in C# (.NET 10), the main logic is in `src/D365BCAdminMCP/`.
- The server exposes 52 administrative tools for environments, apps, sessions, PTE uploads, and extension management.
- Authentication uses Microsoft Entra ID (Azure CLI / Visual Studio credentials, falling back to browser-based login with an encrypted persistent token cache; no credentials stored).

## Key Architecture & Patterns
- **Entry Point:** `src/D365BCAdminMCP/Program.cs` initializes the MCP server and handles command routing.
- **Service Layer:** `D365BCAdminService` is a partial class: `D365BCAdminService.cs` holds authentication and the shared HTTP helper (retries, readable API errors), the other `D365BCAdminService.*.cs` files hold the tools per API area. Models are in `D365BCAdminModels.cs`.
- **Logging:** stdout carries only MCP JSON-RPC messages; always log through `Log` (stderr), never `Console.WriteLine`.
- **Tool annotations:** every `[McpServerTool]` must declare `ReadOnly` or `Destructive` (enforced by `ToolAnnotationTests`).
- **Configuration:** No hardcoded secrets; all authentication is interactive and secure.
- **Multi-Tenant:** Designed to manage multiple BC tenants in parallel.
- **Natural Language:** All commands are mapped from natural language to MCP operations.

## Developer Workflows
- **Build:**
  - Use `npm install -g @demiliani/d365bc-admin-mcp` for global CLI install.
  - For local dev: `dotnet build src/D365BCAdminMCP/D365BCAdminMCP.csproj`; tests: `dotnet test tests/D365BCAdminMCP.Tests`.
- **Run:**
  - Start with `d365bc-admin-mcp` (after build/install).
- **Authentication:**
  - On first run, browser-based Microsoft login may be required; the sign-in is kept across restarts in the encrypted OS token cache.
  - When automatic login fails, run `force_interactive_auth` to open the Microsoft Entra browser prompt for a tenant and choose the account.
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
  - `src/D365BCAdminMCP/D365BCAdminService*.cs` (auth, HTTP helper, tools)
  - `src/D365BCAdminMCP/D365BCAdminModels.cs` (data models)
  - `tests/D365BCAdminMCP.Tests/` (xUnit tests)
  - `scripts/build-dotnet.js`, `scripts/smoke-test.js` (build and MCP smoke test)
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
