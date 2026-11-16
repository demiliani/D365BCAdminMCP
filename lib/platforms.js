'use strict';

// Each platform's self-contained server ships in its own npm package; npm installs only the one whose os/cpu
// match the machine. The release workflow builds one binary per entry.
const MAIN_PACKAGE = '@demiliani/d365bc-admin-mcp';

const PLATFORMS = [
  { rid: 'win-x64', os: 'win32', cpu: 'x64' },
  { rid: 'win-arm64', os: 'win32', cpu: 'arm64' },
  { rid: 'linux-x64', os: 'linux', cpu: 'x64' },
  { rid: 'linux-arm64', os: 'linux', cpu: 'arm64' },
  { rid: 'osx-x64', os: 'darwin', cpu: 'x64' },
  { rid: 'osx-arm64', os: 'darwin', cpu: 'arm64' }
];

function packageName(rid) {
  return `${MAIN_PACKAGE}-${rid}`;
}

function executableName(rid) {
  return rid.startsWith('win') ? 'D365BCAdminMCP.exe' : 'D365BCAdminMCP';
}

function currentPlatform() {
  return PLATFORMS.find((p) => p.os === process.platform && p.cpu === process.arch);
}

// .NET's linux-* runtimes need glibc; Alpine and other musl distributions would need linux-musl-* builds.
function isMusl() {
  if (process.platform !== 'linux') return false;
  const report = process.report && process.report.getReport();
  return Boolean(report) && !report.header.glibcVersionRuntime;
}

module.exports = { MAIN_PACKAGE, PLATFORMS, packageName, executableName, currentPlatform, isMusl };
