#!/usr/bin/env node

const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');
const os = require('os');

// Keep in sync with the release workflow matrix and package.json "files".
const SUPPORTED_RUNTIMES = ['win-x64', 'linux-x64', 'osx-arm64'];

function getRuntimeId() {
  const arch = os.arch();
  switch (os.platform()) {
    case 'win32':
      return arch === 'arm64' ? 'win-arm64' : 'win-x64';
    case 'darwin':
      return arch === 'arm64' ? 'osx-arm64' : 'osx-x64';
    case 'linux':
      return arch === 'arm64' ? 'linux-arm64' : 'linux-x64';
    default:
      return `${os.platform()}-${arch}`;
  }
}

if (process.argv[2] === '--version' || process.argv[2] === '-v') {
  console.log(require('../package.json').version);
  process.exit(0);
}

const rid = getRuntimeId();
const exeName = rid.startsWith('win') ? 'D365BCAdminMCP.exe' : 'D365BCAdminMCP';
const exePath = path.join(__dirname, '..', 'build', rid, exeName);

if (!fs.existsSync(exePath)) {
  console.error(`D365BC Admin MCP Server: no executable for this platform (${rid}).`);
  console.error(`Supported platforms: ${SUPPORTED_RUNTIMES.join(', ')}.`);
  if (!SUPPORTED_RUNTIMES.includes(rid)) {
    console.error('Build it from source with the .NET 10 SDK: https://github.com/demiliani/D365BCAdminMCP#-contributing');
  } else {
    console.error('Reinstall the package: npm install -g @demiliani/d365bc-admin-mcp');
  }
  process.exit(1);
}

// stdout carries the MCP protocol, so the server inherits all three standard streams unchanged.
const child = spawn(exePath, process.argv.slice(2), { stdio: 'inherit', shell: false });

child.on('error', (error) => {
  console.error('Failed to start D365BC Admin MCP Server:', error.message);
  process.exit(1);
});

child.on('exit', (code, signal) => {
  process.exit(signal ? 1 : code ?? 0);
});

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => child.kill(signal));
}
