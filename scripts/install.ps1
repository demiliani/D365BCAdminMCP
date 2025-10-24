# PowerShell script to automate the installation of the MCP server

# Define the installation function
function Install-MCPServer {
    param (
        [string]$installPath = "C:\D365BCAdminMCP"
    )

    # Check if the installation path exists
    if (-Not (Test-Path $installPath)) {
        Write-Host "Creating installation directory at $installPath"
        New-Item -ItemType Directory -Path $installPath
    } else {
        Write-Host "Installation directory already exists at $installPath"
    }

    # Copy necessary files to the installation directory
    Write-Host "Copying files to $installPath"
    Copy-Item -Path ".\src\D365BCAdminMCP\*" -Destination $installPath -Recurse

    # Install any required dependencies (if applicable)
    # Example: Install-Module -Name SomeModule -Force

    Write-Host "Installation completed successfully!"
}

# Execute the installation function
Install-MCPServer -installPath "C:\D365BCAdminMCP"