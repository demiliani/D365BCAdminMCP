#!/bin/bash

# MCP Server Installation Script

# Function to display usage
usage() {
    echo "Usage: $0 [options]"
    echo "Options:"
    echo "  -h, --help        Show this help message"
    echo "  -y, --yes         Automatically answer 'yes' to prompts"
}

# Parse command line arguments
AUTO_YES=false
while [[ "$#" -gt 0 ]]; do
    case $1 in
        -h|--help) usage; exit 0 ;;
        -y|--yes) AUTO_YES=true ;;
        *) echo "Unknown option: $1"; usage; exit 1 ;;
    esac
    shift
done

# Function to prompt for confirmation
confirm() {
    if [ "$AUTO_YES" = false ]; then
        read -p "Are you sure you want to proceed? (y/n) " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            echo "Installation aborted."
            exit 1
        fi
    fi
}

# Check for required dependencies
echo "Checking for required dependencies..."
if ! command -v dotnet &> /dev/null; then
    echo "Error: .NET SDK is not installed. Please install it before proceeding."
    exit 1
fi

# Confirm installation
confirm

# Install the MCP server
echo "Installing MCP server..."
dotnet restore src/D365BCAdminMCP/D365BCAdminMCP.csproj
dotnet build src/D365BCAdminMCP/D365BCAdminMCP.csproj

echo "MCP server installation completed successfully."