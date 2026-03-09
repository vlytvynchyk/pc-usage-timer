#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Uninstalls PC Usage Timer: removes exe, firewall rule, URL ACL, auto-start entry, and app data.
#>

param(
    [string]$InstallDir = "$env:LOCALAPPDATA\PcUsageTimer",
    [int]$Port = 7742
)

$ErrorActionPreference = "SilentlyContinue"

Write-Host "PC Usage Timer - Uninstaller" -ForegroundColor Cyan
Write-Host ""

# 1. Remove auto-start registry entry
$regPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
if (Get-ItemProperty -Path $regPath -Name "PcUsageTimer" -ErrorAction SilentlyContinue) {
    Remove-ItemProperty -Path $regPath -Name "PcUsageTimer"
    Write-Host "Removed auto-start registry entry." -ForegroundColor Green
}

# 2. Remove firewall rule
$ruleName = "PcUsageTimer Remote Lock"
if (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue) {
    Remove-NetFirewallRule -DisplayName $ruleName
    Write-Host "Removed firewall rule." -ForegroundColor Green
}

# 3. Remove URL ACL
netsh http delete urlacl url="http://+:$Port/" 2>$null | Out-Null
Write-Host "Removed URL ACL." -ForegroundColor Green

# 4. Remove install directory and app data
if (Test-Path $InstallDir) {
    Remove-Item -Recurse -Force $InstallDir
    Write-Host "Removed install directory: $InstallDir" -ForegroundColor Green
}

Write-Host ""
Write-Host "Uninstall complete." -ForegroundColor Green
