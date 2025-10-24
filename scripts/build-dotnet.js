#!/usr/bin/env node

const { execSync } = require('child_process');
const path = require('path');
const fs = require('fs');
const os = require('os');

const projectPath = path.join(__dirname, '..', 'src', 'D365BCAdminMCP', 'D365BCAdminMCP.csproj');
const outputDir = path.join(__dirname, '..', 'build');

console.log('🔨 Building D365BC Admin MCP Server...');

// Determine the runtime identifier for the current platform
function getCurrentRuntimeId() {
  const platform = os.platform();
  const arch = os.arch();
  
  if (platform === 'win32') {
    return arch === 'x64' ? 'win-x64' : 'win-arm64';
  } else if (platform === 'darwin') {
    return arch === 'arm64' ? 'osx-arm64' : 'osx-x64';
  } else if (platform === 'linux') {
    return arch === 'arm64' ? 'linux-arm64' : 'linux-x64';
  }
  
  throw new Error(`Unsupported platform: ${platform}-${arch}`);
}

try {
  const rid = getCurrentRuntimeId();
  const buildOutput = path.join(outputDir, rid);
  
  console.log(`📦 Building for ${rid}...`);
  
  // Ensure output directory exists
  if (!fs.existsSync(outputDir)) {
    fs.mkdirSync(outputDir, { recursive: true });
  }
  
  // Build self-contained executable
  execSync(
    `dotnet publish "${projectPath}" -c Release -r ${rid} --self-contained true -p:PublishSingleFile=true -o "${buildOutput}"`,
    { 
      stdio: 'inherit',
      encoding: 'utf-8'
    }
  );
  
  console.log('✅ Build completed successfully!');
  console.log(`📂 Output: ${buildOutput}`);
  
} catch (error) {
  console.error('❌ Build failed:', error.message);
  console.error('');
  console.error('Please ensure:');
  console.error('  1. .NET SDK 9.0 or higher is installed');
  console.error('  2. All dependencies are available');
  console.error('  3. The project builds successfully with: dotnet build');
  process.exit(1);
}
