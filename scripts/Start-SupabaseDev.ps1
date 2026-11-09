#Requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9-]+\.pooler\.supabase\.com$')]
    [string]$PoolerHost,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9_]+\.[a-z0-9]+$')]
    [string]$DatabaseUser,

    [Parameter(Mandatory)]
    [string]$BackendClientId,

    [string]$RootCertificate,
    [switch]$ApplyMigrations
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$ports = [ordered]@{ Users = 5101; Attractions = 5104; Transport = 5105; Accommodations = 5106; Settings = 5107; Trips = 5108; Frontend = 5100 }

$listeners = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
if ($listeners.Port | Where-Object { $_ -in $ports.Values }) {
    throw 'A TravelTracker port is already in use. Stop the existing stack first.'
}

$authority = $env:Authentication__Bearer__Authority -as [uri]
if (!$authority -or !$authority.IsAbsoluteUri -or $authority.Scheme -ne 'https' -or
    !$env:Authentication__Bearer__Issuer -or !$env:Authentication__Bearer__Audience) {
    throw 'Set the Auth0 bearer authority (HTTPS), issuer, and audience before starting the stack.'
}

dotnet dev-certs https --check --trust
if ($LASTEXITCODE) { throw 'Trust the Users development certificate with dotnet dev-certs https --trust.' }
dotnet build (Join-Path $repo 'TravelTracker.slnx')
if ($LASTEXITCODE) { throw 'TravelTracker build failed.' }

$backendSecret = Read-Host 'Auth0 machine-to-machine client secret' -AsSecureString
try {
    $tokenUri = $authority.AbsoluteUri.TrimEnd('/') + '/oauth/token'
    $grant = @{
        grant_type = 'client_credentials'
        client_id = $BackendClientId
        client_secret = [pscredential]::new('auth0', $backendSecret).GetNetworkCredential().Password
        audience = $env:Authentication__Bearer__Audience
        scope = 'users.access'
    }
    $tokenResponse = Invoke-RestMethod -Uri $tokenUri -Method Post -ContentType 'application/json' `
        -Body ($grant | ConvertTo-Json -Compress) -MaximumRedirection 0 -TimeoutSec 20
}
catch {
    throw 'Auth0 could not issue the backend access token. Check the authority, audience, client credentials, and users.access grant.'
}
finally {
    if ($grant) { $grant.Clear() }
    $backendSecret.Dispose()
}

$token = $tokenResponse.access_token
if ($token -notmatch '^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$') {
    throw 'Auth0 did not return a JWT. Check the configured API audience.'
}
$payload = $token.Split('.')[1].Replace('-', '+').Replace('_', '/')
while ($payload.Length % 4) { $payload += '=' }
$claims = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
if (!$claims.sub -or 'users.access' -notin ($claims.scope -split ' ') -or
    $claims.exp -le [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) {
    throw 'The backend token must be current and include users.access.'
}

$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
$connection['Host'] = $PoolerHost
$connection['Port'] = 5432
$connection['Username'] = $DatabaseUser
$connection['SSL Mode'] = 'VerifyFull'
$connection['Maximum Pool Size'] = 5
if ($RootCertificate) { $connection['Root Certificate'] = (Resolve-Path -LiteralPath $RootCertificate).Path }
$password = Read-Host 'Supabase database password' -AsSecureString
try {
    if (!$password.Length) { throw 'A Supabase database password is required.' }
    $connection['Password'] = [pscredential]::new($DatabaseUser, $password).GetNetworkCredential().Password
}
finally { $password.Dispose() }

$logs = Join-Path ([IO.Path]::GetTempPath()) "TravelTracker-Supabase-$([guid]::NewGuid())"
New-Item -ItemType Directory -Path $logs | Out-Null
$processes = @()
try {
    foreach ($service in $ports.Keys) {
        $project = if ($service -eq 'Frontend') { 'TravelTracker.Frontend' } else { "TravelTracker.$service.Api" }
        $folder = Join-Path $repo "src/$project"
        $dll = Join-Path $folder "bin/Debug/net10.0/$project.dll"
        $scheme = if ($service -eq 'Users') { 'https' } else { 'http' }
        $origin = "${scheme}://localhost:$($ports[$service])"
        $settings = @{
            ASPNETCORE_ENVIRONMENT = 'Development'
            DOTNET_ENVIRONMENT = 'Development'
            ASPNETCORE_URLS = $origin
        }
        if ($service -ne 'Frontend') {
            $connection['Database'] = 'traveltracker_' + $service.ToLowerInvariant()
            $settings["ConnectionStrings__$service"] = $connection.ConnectionString
            $settings['Database__ApplyMigrations'] = $ApplyMigrations.IsPresent.ToString()
            $settings['Database__SeedExamples'] = 'false'
        }
        switch ($service) {
            'Users' { $settings['UsersAccess__AllowedServiceSubjects__0'] = $claims.sub }
            'Trips' {
                $settings['TripReferences__UsersBaseUrl'] = 'https://localhost:5101'
                $settings['TripReferences__UsersServiceAccessToken'] = $token
            }
            { $_ -in 'Attractions', 'Accommodations', 'Settings' } {
                $settings['Users__InternalBaseUrl'] = 'https://localhost:5101'
                $settings['Users__ServiceAccessToken'] = $token
            }
        }

        $process = Start-Process dotnet -ArgumentList ('"{0}"' -f $dll) -WorkingDirectory $folder `
            -Environment $settings -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $logs "$service.out.log") `
            -RedirectStandardError (Join-Path $logs "$service.err.log")
        $processes += $process
        $route = if ($service -eq 'Frontend') { '/' } else { '/health' }
        $deadline = [DateTime]::UtcNow.AddSeconds(60)
        do {
            if ($process.HasExited) { throw "$service exited during startup. See $logs." }
            $ready = $false
            try {
                $result = Invoke-WebRequest "$origin$route" -NoProxy -TimeoutSec 2 -MaximumRedirection 0
                $ready = !$process.HasExited -and ($service -eq 'Frontend' -or
                    (ConvertFrom-Json $result.Content).service -match "(^|\.)$service$")
            }
            catch { Start-Sleep -Milliseconds 250 }
        } until ($ready -or [DateTime]::UtcNow -ge $deadline)
        if (!$ready) { throw "$service did not become ready at $origin$route. See $logs." }
    }

    Write-Host "TravelTracker is running at http://localhost:5100. Logs: $logs"
    $expires = [DateTimeOffset]::FromUnixTimeSeconds($claims.exp).ToLocalTime()
    Write-Host "Restart the launcher before the backend access token expires at $expires."
    [void](Read-Host 'Press Enter to stop TravelTracker')
}
finally {
    $connection.Clear()
    foreach ($process in $processes) {
        if (!$process.HasExited) { Stop-Process -InputObject $process -ErrorAction SilentlyContinue }
        $process.Dispose()
    }
}
