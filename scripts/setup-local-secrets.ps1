# Run in your own terminal. Passwords are never written to repository files.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot '../src/MyApp.API/MyApp.API.csproj'
$settingsPath = Join-Path $PSScriptRoot '../src/MyApp.API/appsettings.json'
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
$connection.set_ConnectionString($settings.ConnectionStrings.DefaultConnection)
if (-not $connection.get_ConnectionString()) { throw 'DefaultConnection is not configured in appsettings.json.' }

Write-Host 'Configure the password for the PostgreSQL connection from appsettings.json.'
Write-Host 'The password will be saved in local .NET User Secrets, outside the repository.'
$password = Read-Host 'PostgreSQL password' -AsSecureString
if ($password.Length -eq 0) { throw 'The password cannot be empty.' }
$pointer = [IntPtr]::Zero
try {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($password)
    $connection['Password'] = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    # Use standard input so the password is not included in process arguments.
    $savedConnection = $connection.get_ConnectionString()
    $verification = [System.Data.Common.DbConnectionStringBuilder]::new()
    $verification.set_ConnectionString($savedConnection)
    if (-not $verification.ContainsKey('Password') -or [string]::IsNullOrWhiteSpace([string]$verification['Password'])) {
        throw 'Password was not added to the connection string. Nothing was saved.'
    }
    @{ 'ConnectionStrings:DefaultConnection' = $savedConnection } |
        ConvertTo-Json -Compress |
        dotnet user-secrets set --project $projectPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not save local configuration.' }
    Write-Host 'Database password saved. You can now start the API.'
} finally {
    if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    $connection.Clear()
    if ($verification) { $verification.Clear() }
    $savedConnection = $null
    $password.Dispose()
}
