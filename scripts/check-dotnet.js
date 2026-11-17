#!/usr/bin/env node

const { execSync } = require('child_process');
const os = require('os');

console.log('🔍 Checking .NET SDK availability...');

try {
  const version = execSync('dotnet --version', { 
    stdio: 'pipe',
    encoding: 'utf-8' 
  }).trim();
  
  console.log(`✅ .NET SDK ${version} found`);
  
  const majorVersion = parseInt(version.split('.')[0]);
  if (majorVersion < 10) {
    console.warn('⚠️  Warning: building from source requires .NET SDK 10.0 or higher.');
    console.warn('   Current version:', version);
  }
} catch (error) {
  console.error('❌ .NET SDK not found!');
  console.error('');
  console.error('The D365BC Admin MCP Server requires .NET SDK 10.0 or higher to build.');
  console.error('');
  console.error('Please install .NET SDK from:');
  console.error('  https://dotnet.microsoft.com/download');
  console.error('');
  
  if (os.platform() === 'darwin') {
    console.error('On macOS, you can install via Homebrew:');
    console.error('  brew install --cask dotnet-sdk');
  } else if (os.platform() === 'linux') {
    console.error('On Linux, follow the instructions for your distribution:');
    console.error('  https://learn.microsoft.com/dotnet/core/install/linux');
  } else if (os.platform() === 'win32') {
    console.error('On Windows, download the installer from:');
    console.error('  https://dotnet.microsoft.com/download');
  }
  
  process.exit(1);
}
