#!/usr/bin/env node

// Starts the MCP server, performs the MCP handshake and checks that the tools are exposed.
// Usage: node scripts/smoke-test.js [executable]   (defaults to the npm launcher)
// No network access or sign-in is needed: tools/list does not call Business Central.

const { spawn } = require('child_process');
const path = require('path');
const readline = require('readline');

const REQUIRED_TOOLS = ['get_environment_informations', 'install_pte', 'delete_environment', 'start_database_export'];
const MIN_TOOLS = 50;
const TIMEOUT_MS = 60000;

const command = process.argv[2]
  ? [path.resolve(process.argv[2])]
  : [process.execPath, path.join(__dirname, '..', 'bin', 'd365bc-admin-mcp.js')];

const server = spawn(command[0], command.slice(1), { stdio: ['pipe', 'pipe', 'inherit'] });
const pending = new Map();
let nextId = 1;

function fail(message) {
  console.error(`Smoke test FAILED: ${message}`);
  server.kill();
  process.exit(1);
}

const timer = setTimeout(() => fail(`no answer within ${TIMEOUT_MS / 1000}s`), TIMEOUT_MS);
server.on('error', (error) => fail(`cannot start ${command.join(' ')}: ${error.message}`));
server.on('exit', (code) => fail(`server exited early with code ${code}`));

readline.createInterface({ input: server.stdout }).on('line', (line) => {
  let message;
  try {
    message = JSON.parse(line);
  } catch {
    fail(`stdout must carry only JSON-RPC messages, got: ${line.slice(0, 200)}`);
  }
  const resolve = pending.get(message.id);
  if (resolve) {
    pending.delete(message.id);
    resolve(message);
  }
});

function request(method, params) {
  const id = nextId++;
  server.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
  return new Promise((resolve) => pending.set(id, resolve));
}

(async () => {
  const init = await request('initialize', {
    protocolVersion: '2025-06-18',
    capabilities: {},
    clientInfo: { name: 'smoke-test', version: '1.0.0' }
  });
  if (!init.result) fail(`initialize: ${JSON.stringify(init.error ?? init)}`);
  server.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');

  const list = await request('tools/list', {});
  if (!list.result?.tools) fail(`tools/list: ${JSON.stringify(list.error ?? list)}`);
  const names = list.result.tools.map((tool) => tool.name);
  const missing = REQUIRED_TOOLS.filter((name) => !names.includes(name));
  if (names.length < MIN_TOOLS || missing.length > 0) {
    fail(`expected at least ${MIN_TOOLS} tools including ${REQUIRED_TOOLS.join(', ')}; got ${names.length}, missing: ${missing.join(', ') || 'none'}`);
  }

  console.log(`Smoke test passed: ${init.result.serverInfo.name} ${init.result.serverInfo.version}, protocol ${init.result.protocolVersion}, ${names.length} tools`);
  clearTimeout(timer);
  server.removeAllListeners('exit');
  server.kill();
  process.exit(0);
})();
