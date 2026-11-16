#!/usr/bin/env node

const { execSync } = require('child_process');
const path = require('path');
const fs = require('fs');
const { currentPlatform, executableName } = require('../lib/platforms');

const projectPath = path.join(__dirname, '..', 'src', 'D365BCAdminMCP', 'D365BCAdminMCP.csproj');
const outputDir = path.join(__dirname, '..', 'build');

console.log('🔨 Building D365BC Admin MCP Server...');

try {
  const platform = currentPlatform();
  if (!platform) {
    throw new Error(`Unsupported platform: ${process.platform}-${process.arch}`);
  }
  const rid = platform.rid;
  const buildOutput = path.join(outputDir, rid);
  const exeName = executableName(rid);

  console.log(`📦 Building for ${rid}...`);
  
  // Ensure output directory exists
  if (!fs.existsSync(outputDir)) {
    fs.mkdirSync(outputDir, { recursive: true });
  }
  
  // Same command as the release workflow; single-file, self-contained and trimming are set in the csproj.
  execSync(
    `dotnet publish "${projectPath}" -c Release -r ${rid} -o "${buildOutput}"`,
    { 
      stdio: 'inherit',
      encoding: 'utf-8'
    }
  );
  
  console.log('✅ Build completed successfully!');
  console.log(`📂 Output: ${path.join(buildOutput, exeName)}`);
  
} catch (error) {
  console.error('❌ Build failed:', error.message);
  console.error('');
  console.error('Please ensure:');
  console.error('  1. .NET SDK 10.0 or higher is installed');
  console.error('  2. All dependencies are available');
  console.error('  3. The project builds successfully with: dotnet build');
  console.error('');
  console.error('If you are installing from npm, pre-built binaries should be included.');
  console.error('This build step should only be needed for development.');
  process.exit(1);
}
