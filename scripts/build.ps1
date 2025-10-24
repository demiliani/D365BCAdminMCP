# PowerShell script to build the D365BCAdminMCP project

# Navigate to the project directory
cd "$(dirname $PSScriptRoot)"

# Restore the project dependencies
dotnet restore src/D365BCAdminMCP/D365BCAdminMCP.csproj

# Build the project
dotnet build src/D365BCAdminMCP/D365BCAdminMCP.csproj -c Release

# Output the build results
if ($LASTEXITCODE -eq 0) {
    Write-Host "Build completed successfully."
} else {
    Write-Host "Build failed. Please check the errors above."
    exit $LASTEXITCODE
}