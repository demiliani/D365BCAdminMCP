# Security Policy

This MCP server signs in to Microsoft Entra ID and administers Business Central tenants, including production environments, so security reports are taken seriously.

## Supported versions

Only the latest release published on npm (`@demiliani/d365bc-admin-mcp`) receives security fixes.

## Reporting a vulnerability

Please **do not open a public issue**. Report it privately through GitHub instead:
[Report a vulnerability](https://github.com/demiliani/D365BCAdminMCP/security/advisories/new) (repository **Security** tab → **Report a vulnerability**).

Include what an attacker can do, the affected version, and the steps or a proof of concept to reproduce it. You will get an answer within a few working days; once a fix is released, the advisory is published with credit to the reporter unless you prefer otherwise.

## Verifying what you install

- npm packages are published from GitHub Actions through npm trusted publishing and carry a provenance attestation: `npm audit signatures` checks it after installing.
- The binaries attached to GitHub releases have build provenance attestations: `gh attestation verify <file> --repo demiliani/D365BCAdminMCP`.

## Scope

In scope: this repository's code, its npm packages and release binaries. Issues in Business Central, the Admin Center API or Microsoft Entra ID should be reported to Microsoft through the [Microsoft Security Response Center](https://msrc.microsoft.com/report).
