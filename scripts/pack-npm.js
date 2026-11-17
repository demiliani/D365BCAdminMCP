#!/usr/bin/env node

// Builds the npm tarballs for a release: one package per platform with its server binary, plus the main
// package (launcher only) that lists them as optionalDependencies pinned to the same version.
// Usage: node scripts/pack-npm.js <binaries-dir> <out-dir>
//   <binaries-dir>/<rid>/<executable> must exist for every platform in lib/platforms.js.

const { execFileSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { MAIN_PACKAGE, PLATFORMS, packageName, executableName } = require('../lib/platforms');

const root = path.join(__dirname, '..');
const [binariesDir, outDir] = process.argv.slice(2).map((p) => p && path.resolve(p));
if (!binariesDir || !outDir) {
  console.error('Usage: node scripts/pack-npm.js <binaries-dir> <out-dir>');
  process.exit(1);
}

const main = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
fs.mkdirSync(outDir, { recursive: true });
// Staged outside the repository: inside it, npm would treat each package as part of the root project.
const stageRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'd365bc-pack-'));

const missing = PLATFORMS.filter((p) => !fs.existsSync(path.join(binariesDir, p.rid, executableName(p.rid))));
if (missing.length > 0) {
  console.error(`Missing binaries: ${missing.map((p) => path.join(binariesDir, p.rid, executableName(p.rid))).join(', ')}`);
  process.exit(1);
}

function writeJson(file, value) {
  fs.writeFileSync(file, JSON.stringify(value, null, 2) + '\n');
}

function pack(dir) {
  const output = JSON.parse(execFileSync('npm', ['pack', '--json', '--pack-destination', outDir], {
    cwd: dir,
    encoding: 'utf8',
    shell: process.platform === 'win32'
  }));
  const result = Array.isArray(output) ? output[0] : Object.values(output)[0];
  console.log(`${result.filename}  ${(result.size / 1024 / 1024).toFixed(1)} MB  (${result.files.map((f) => f.path).join(', ')})`);
  return { name: result.name, version: result.version, filename: result.filename };
}

const shared = {
  version: main.version,
  author: main.author,
  license: main.license,
  homepage: main.homepage,
  repository: main.repository,
  bugs: main.bugs,
  publishConfig: { access: 'public' }
};

const tarballs = [];
for (const platform of PLATFORMS) {
  const name = packageName(platform.rid);
  const exe = executableName(platform.rid);
  const dir = path.join(stageRoot, platform.rid);
  fs.mkdirSync(dir);
  fs.copyFileSync(path.join(binariesDir, platform.rid, exe), path.join(dir, exe));
  fs.chmodSync(path.join(dir, exe), 0o755);
  fs.copyFileSync(path.join(root, 'LICENSE'), path.join(dir, 'LICENSE'));
  fs.writeFileSync(path.join(dir, 'README.md'),
    `# ${name}\n\nThe ${platform.rid} server binary of [${MAIN_PACKAGE}](https://www.npmjs.com/package/${MAIN_PACKAGE}). ` +
    `Install that package instead: npm picks this one automatically on ${platform.rid}.\n`);
  writeJson(path.join(dir, 'package.json'), {
    name,
    description: `${platform.rid} binary of ${MAIN_PACKAGE}`,
    ...shared,
    os: [platform.os],
    cpu: [platform.cpu],
    ...(platform.os === 'linux' ? { libc: ['glibc'] } : {}),
    files: [exe]
  });
  tarballs.push(pack(dir));
}

const mainDir = path.join(stageRoot, 'main');
fs.mkdirSync(mainDir);
fs.cpSync(path.join(root, 'bin'), path.join(mainDir, 'bin'), { recursive: true });
fs.cpSync(path.join(root, 'lib'), path.join(mainDir, 'lib'), { recursive: true });
fs.copyFileSync(path.join(root, 'README.md'), path.join(mainDir, 'README.md'));
fs.copyFileSync(path.join(root, 'LICENSE'), path.join(mainDir, 'LICENSE'));
const { scripts, devDependencies, ...manifest } = main;
manifest.optionalDependencies = Object.fromEntries(PLATFORMS.map((p) => [packageName(p.rid), main.version]));
writeJson(path.join(mainDir, 'package.json'), manifest);
tarballs.push(pack(mainDir));

// Publish order: platform packages first, so the main package never points at versions that don't exist yet.
writeJson(path.join(outDir, 'publish-order.json'), tarballs);
fs.rmSync(stageRoot, { recursive: true, force: true });
