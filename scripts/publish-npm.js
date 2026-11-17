#!/usr/bin/env node

// Stages the tarballs built by pack-npm.js on npm, platform packages first and the main package last.
// Versions already on the registry are skipped, so a failed run can be re-run after fixing the cause.
// Usage: node scripts/publish-npm.js <tarball-dir> [--tag <dist-tag>] [--dry-run]

const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const args = process.argv.slice(2);
const dir = args[0] && path.resolve(args[0]);
if (!dir) {
  console.error('Usage: node scripts/publish-npm.js <tarball-dir> [--tag <dist-tag>] [--dry-run]');
  process.exit(1);
}
const tagIndex = args.indexOf('--tag');
const extra = [
  ...(tagIndex > 0 ? ['--tag', args[tagIndex + 1]] : []),
  ...(args.includes('--dry-run') ? ['--dry-run'] : [])
];

function npm(npmArgs, options = {}) {
  return spawnSync('npm', npmArgs, { encoding: 'utf8', shell: process.platform === 'win32', ...options });
}

function isPublished(name, version) {
  // Exit code 1 (E404) for an unknown package, empty output for an unknown version of a known package.
  const result = npm(['view', `${name}@${version}`, 'version', '--json']);
  return result.status === 0 && result.stdout.trim() !== '';
}

const order = JSON.parse(fs.readFileSync(path.join(dir, 'publish-order.json'), 'utf8'));
for (const { name, version, filename } of order) {
  if (isPublished(name, version)) {
    console.log(`${name}@${version} is already on npm, skipping`);
    continue;
  }
  console.log(`Staging ${name}@${version}`);
  const result = npm(['stage', 'publish', path.join(dir, filename), ...extra], { stdio: 'inherit' });
  if (result.status !== 0) {
    console.error(`Staging ${name}@${version} failed. A package that does not exist on npm yet must be published once by hand ` +
      'before a trusted publisher can be configured for it (see README, "Releasing").');
    process.exit(1);
  }
}
