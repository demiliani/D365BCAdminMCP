#!/bin/bash

# Build script for D365BCAdminMCP

# Exit immediately if a command exits with a non-zero status
set -e

# Define the project directory
PROJECT_DIR="$(dirname "$(dirname "$(realpath "$0")")")"

# Navigate to the project directory
cd "$PROJECT_DIR/src/D365BCAdminMCP"

# Restore dependencies
echo "Restoring dependencies..."
dotnet restore

# Build the project
echo "Building the project..."
dotnet build --configuration Release

# Output the build results
echo "Build completed successfully."