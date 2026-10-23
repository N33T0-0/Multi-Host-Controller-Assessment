<#
.SYNOPSIS
    Generates a cryptographically random 32-byte secret for a Client Agent and outputs
    the Base64URL string for the agent and the SHA-256 BINARY(32) SQL INSERT statement.
#>

$bytes = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)

# Base64URL encoding (without padding, url safe)
$base64 = [Convert]::ToBase64String($bytes)
$base64Url = $base64.TrimEnd('=').Replace('+', '-').Replace('/', '_')

# SHA-256 hash
$sha256 = [System.Security.Cryptography.SHA256]::Create()
$hash = $sha256.ComputeHash($bytes)
$hex = ($hash | ForEach-Object { $_.ToString("x2") }) -join ""

Write-Host "================================================================="
Write-Host "CLIENT AGENT CREDENTIAL PROVISIONING"
Write-Host "================================================================="
Write-Host ""
Write-Host "[1] AGENT SECRET (Distribute securely to Client Agent):"
Write-Host $base64Url -ForegroundColor Green
Write-Host ""
Write-Host "[2] DATABASE RECORD (Run this SQL script against Master Host DB):"
$sql = "INSERT INTO dbo.ClientAgentCredentials (ClientHostID, SecretHash, CreatedAt) VALUES (NULL, 0x$hex, SYSUTCDATETIME());"
Write-Host $sql -ForegroundColor Yellow
Write-Host ""
Write-Host "================================================================="

