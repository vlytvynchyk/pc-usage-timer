#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs PC Usage Timer: copies the exe, adds firewall rule, and registers HTTP URL ACL.
.DESCRIPTION
    Run this script once as Administrator after publishing the exe.
    It sets up everything needed for the remote lock feature to work without prompts.
#>

param(
    [string]$ExePath = ".\publish\PcUsageTimer.exe",
    [string]$InstallDir = "$env:LOCALAPPDATA\PcUsageTimer",
    [int]$Port = 7742
)

$ErrorActionPreference = "Stop"

Write-Host "PC Usage Timer - Installer" -ForegroundColor Cyan
Write-Host ""

# 1. Copy exe to install directory
if (-not (Test-Path $ExePath)) {
    Write-Host "ERROR: $ExePath not found. Run 'dotnet publish -c Release -o ./publish' first." -ForegroundColor Red
    exit 1
}

Write-Host "Creating install directory: $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

Write-Host "Copying PcUsageTimer.exe..."
Copy-Item -Path $ExePath -Destination "$InstallDir\PcUsageTimer.exe" -Force

# 2. Add Windows Firewall rule
$ruleName = "PcUsageTimer Remote Lock"
$existing = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Firewall rule already exists, updating..."
    Remove-NetFirewallRule -DisplayName $ruleName
}
Write-Host "Adding firewall rule for port $Port..."
New-NetFirewallRule -DisplayName $ruleName `
    -Direction Inbound `
    -Protocol TCP `
    -LocalPort $Port `
    -Action Allow `
    -Profile Private `
    -Description "Allows PC Usage Timer remote lock from phones on private networks" | Out-Null
Write-Host "  Firewall rule added." -ForegroundColor Green

# 3. Register HTTP URL ACL (so the app doesn't need admin to listen)
Write-Host "Registering HTTP URL reservation for port $Port..."
$urlAcl = "http://+:$Port/"
# Remove existing reservation if present
netsh http delete urlacl url=$urlAcl 2>$null | Out-Null
netsh http add urlacl url=$urlAcl user=Everyone | Out-Null
Write-Host "  URL ACL registered." -ForegroundColor Green

# 4. Summary
Write-Host ""
Write-Host "Installation complete!" -ForegroundColor Green
Write-Host "  Installed to: $InstallDir\PcUsageTimer.exe"
Write-Host "  Firewall rule: $ruleName (port $Port, private networks)"
Write-Host "  URL ACL: $urlAcl"
Write-Host ""
Write-Host "To start: run $InstallDir\PcUsageTimer.exe" -ForegroundColor Cyan
Write-Host "To auto-start with Windows: check 'Start with Windows' in the app." -ForegroundColor Cyan
