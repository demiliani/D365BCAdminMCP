#!/usr/bin/env node

const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');
const { MAIN_PACKAGE, PLATFORMS, packageName, executableName, currentPlatform, isMusl } = require('../lib/platforms');

if (process.argv[2] === '--version' || process.argv[2] === '-v') {
  console.log(require('../package.json').version);
  process.exit(0);
}

function fail(lines) {
  for (const line of lines) console.error(line);
  process.exit(1);
}

const platform = currentPlatform();
if (!platform || isMusl()) {
  fail([
    `D365BC Admin MCP Server does not ship a binary for ${process.platform}-${process.arch}${isMusl() ? ' (musl libc)' : ''}.`,
    `Supported platforms: ${PLATFORMS.map((p) => p.rid).join(', ')} (Linux needs glibc).`,
    'Build it from source with the .NET 10 SDK: https://github.com/demiliani/D365BCAdminMCP#-contributing'
  ]);
}

function findExecutable(rid) {
  const exe = executableName(rid);
  try {
    const candidate = path.join(path.dirname(require.resolve(`${packageName(rid)}/package.json`)), exe);
    if (fs.existsSync(candidate)) return candidate;
  } catch {
    // Not installed: fall back to a source checkout built with `npm run build`.
  }
  const local = path.join(__dirname, '..', 'build', rid, exe);
  return fs.existsSync(local) ? local : null;
}

const exePath = findExecutable(platform.rid);
if (!exePath) {
  fail([
    `D365BC Admin MCP Server: the package ${packageName(platform.rid)} with the server for ${platform.rid} is not installed.`,
    'npm skips it when optional dependencies are omitted (--omit=optional, --no-optional). Reinstall with:',
    `  npm install -g ${MAIN_PACKAGE}`
  ]);
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
