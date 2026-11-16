#!/usr/bin/env node

// Keeps server.json (MCP Registry metadata) on the version and name in package.json.
// Runs automatically from `npm version`; `--check` fails instead of writing when they differ.

const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const pkg = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
const file = path.join(root, 'server.json');
const server = JSON.parse(fs.readFileSync(file, 'utf8'));
const npmPackage = server.packages.find((p) => p.registryType === 'npm' && p.identifier === pkg.name);

const problems = [];
if (server.name !== pkg.mcpName) problems.push(`server.json name ${server.name} != package.json mcpName ${pkg.mcpName}`);
if (!npmPackage) problems.push(`server.json has no npm package ${pkg.name}`);
if (problems.length > 0) {
  console.error(problems.join('\n'));
  process.exit(1);
}

if (server.version === pkg.version && npmPackage.version === pkg.version) {
  console.log(`server.json is on version ${pkg.version}`);
  process.exit(0);
}
if (process.argv.includes('--check')) {
  console.error(`server.json is on version ${server.version} but package.json is on ${pkg.version}: run node scripts/sync-server-json.js`);
  process.exit(1);
}
server.version = pkg.version;
npmPackage.version = pkg.version;
fs.writeFileSync(file, JSON.stringify(server, null, 2) + '\n');
console.log(`server.json updated to version ${pkg.version}`);
