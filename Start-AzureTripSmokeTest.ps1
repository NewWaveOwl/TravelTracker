param(
    [string]$Repository = $PSScriptRoot,
    [switch]$RunOnly,
    [switch]$SkipNuGetAudit
)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $Repository
$ports = [ordered]@{ Users=5101; Attractions=5104; Transport=5105; Accommodations=5106; Settings=5107; Trips=5108; Frontend=5100 }
if (Get-NetTCPConnection -State Listen -LocalPort ([int[]]@($ports.Values)) -ErrorAction SilentlyContinue) {
    throw 'A TravelTracker port is already in use. Stop the existing stack first.'
}
dotnet dev-certs https --check --trust
if ($LASTEXITCODE) { throw 'Run dotnet dev-certs https --trust first.' }

$settings = Get-Content -Raw -LiteralPath (Join-Path $env:APPDATA 'Microsoft\UserSecrets\traveltracker-users\secrets.json') | ConvertFrom-Json
foreach ($setting in 'Authority', 'Issuer', 'Audience') {
    $value = $settings."Authentication:Bearer:$setting"
    if (-not $value) { throw "Missing Users bearer setting $setting." }
    Set-Item "Env:Authentication__Bearer__$setting" $value
}

$databaseUser = (Read-Host 'Enter your assigned Azure PostgreSQL username').Trim()
if (-not $databaseUser) { throw 'The Azure PostgreSQL username is required.' }
$databasePassword = Read-Host "Enter the Azure PostgreSQL password for $databaseUser" -AsSecureString
if ($databasePassword.Length -eq 0) { throw 'The Azure PostgreSQL password is required.' }
$testApplicationId = (Read-Host 'Enter the Auth0 backend application Client ID').Trim()
if (-not $testApplicationId) { throw 'The Auth0 backend application Client ID is required.' }
$clientSecret = Read-Host 'Enter the Auth0 backend application Client Secret' -AsSecureString
if ($clientSecret.Length -eq 0) { throw 'The Auth0 client secret is required.' }
$tokenRequest = @{
    client_id     = $testApplicationId
    client_secret = [pscredential]::new('auth0', $clientSecret).GetNetworkCredential().Password
    audience      = 'traveltracker-api'
    grant_type    = 'client_credentials'
    scope         = 'users.access'
}
try {
    $response = Invoke-RestMethod -Method Post -Uri 'https://dev-d523qhusnp72gtz5.us.auth0.com/oauth/token' -ContentType 'application/json' -Body ($tokenRequest | ConvertTo-Json -Compress)
}
catch {
    $status = $_.Exception.Response.StatusCode
    if ($status) { throw "Auth0 token request failed (HTTP $([int]$status)). Check the Test Application client secret and users.access grant." }
    throw 'Could not reach the Auth0 token endpoint.'
}
finally {
    $tokenRequest.Clear()
    $clientSecret.Dispose()
}
$token = $response.access_token
if ($token -notmatch '^eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$') {
    throw 'Auth0 did not return an access JWT.'
}
$parts = $token.Split('.')
$payload = $parts[1].Replace('-', '+').Replace('_', '/')
$payload = $payload.PadRight([int]([Math]::Ceiling($payload.Length / 4.0) * 4), '=')
$claims = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
if (-not $claims.sub -or $claims.exp -le [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() -or 'users.access' -notin ($claims.scope -split ' ')) {
    throw 'The backend JWT must be current and include users.access.'
}
$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
$connection['Host'] = 'traveltracker-jac-dev-pg.postgres.database.azure.com'
$connection['Port'] = 5432
$connection['Username'] = $databaseUser
$connection['Password'] = [pscredential]::new($databaseUser, $databasePassword).GetNetworkCredential().Password
$databasePassword.Dispose()
$connection['SSL Mode'] = 'VerifyFull'

$restoreArguments = @('TravelTracker.slnx')
if ($SkipNuGetAudit) { $restoreArguments += '-p:NuGetAudit=false' }
dotnet restore @restoreArguments
if ($LASTEXITCODE) { throw 'Restore failed.' }
dotnet build TravelTracker.slnx --no-restore
if ($LASTEXITCODE) { throw 'Build failed.' }

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Database__ApplyMigrations = 'false'
$env:Users__InternalBaseUrl = 'https://localhost:5101'

$logs = Join-Path $env:TEMP "TravelTracker-Azure-Test-$PID"
New-Item -ItemType Directory -Path $logs | Out-Null

function Start-TestService([string]$Service) {
    $project = if ($Service -eq 'Frontend') { 'TravelTracker.Frontend' } else { "TravelTracker.$Service.Api" }
    $folder = Join-Path (Get-Location) "src\$project"
    $scheme = if ($Service -eq 'Users') { 'https' } else { 'http' }
    $env:ASPNETCORE_URLS = "$($scheme)://localhost:$($ports[$Service])"
    $key = "Env:ConnectionStrings__$Service"
    if ($Service -ne 'Frontend') {
        $connection['Database'] = "traveltracker_$($Service.ToLowerInvariant())"
        Set-Item -LiteralPath $key -Value $connection.ConnectionString
    }
    $serviceSetting = switch ($Service) {
        'Users' { 'UsersAccess__AllowedServiceSubjects__0' }
        'Trips' { 'TripReferences__UsersServiceAccessToken' }
        { $_ -in 'Attractions', 'Accommodations', 'Settings' } { 'Users__ServiceAccessToken' }
    }
    if ($serviceSetting) {
        $serviceValue = if ($Service -eq 'Users') { $claims.sub } else { $token }
        [Environment]::SetEnvironmentVariable($serviceSetting, $serviceValue, 'Process')
    }
    try {
        $dll = Join-Path $folder "bin\Debug\net10.0\$project.dll"
        Start-Process dotnet -ArgumentList ('"{0}"' -f $dll) -WorkingDirectory $folder -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logs "$Service.out.log") -RedirectStandardError (Join-Path $logs "$Service.err.log")
    }
    finally {
        if ($Service -ne 'Frontend') { Remove-Item -LiteralPath $key }
        if ($serviceSetting) { [Environment]::SetEnvironmentVariable($serviceSetting, $null, 'Process') }
    }
}

Add-Type -AssemblyName System.Net.Http
$healthHandler = [System.Net.Http.HttpClientHandler]::new()
$healthHandler.UseProxy = $false
$healthHandler.AllowAutoRedirect = $false
$healthClient = [System.Net.Http.HttpClient]::new($healthHandler)
$healthClient.Timeout = [TimeSpan]::FromSeconds(1)

function Wait-TestService([string]$Service, $Process) {
    $scheme = if ($Service -eq 'Users') { 'https' } else { 'http' }
    $route = if ($Service -eq 'Frontend') { '/' } else { '/health' }
    $url = "$($scheme)://localhost:$($ports[$Service])$route"
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $probeResult = 'No HTTP response'
    do {
        if ($Process.HasExited) { throw "$Service exited. Check its logs in $logs." }
        try {
            $result = $healthClient.GetAsync($url).GetAwaiter().GetResult()
            try {
                if ($result.IsSuccessStatusCode) { return }
                $probeResult = "HTTP $([int]$result.StatusCode)"
            }
            finally {
                $result.Dispose()
            }
        }
        catch {
            $probeResult = $_.Exception.GetBaseException().Message
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "$Service did not respond successfully at $url ($probeResult). Check its logs in $logs."
}

$processes = @{}
try {
    foreach ($service in $ports.Keys) { $processes[$service] = Start-TestService $service }
    foreach ($service in $ports.Keys) { Wait-TestService $service $processes[$service] }
    Write-Host "All services are listening. Logs: $logs"
    if ($RunOnly) {
        Write-Host 'Open http://localhost:5100 in your browser and sign in.'
        [void](Read-Host 'Press Enter to stop TravelTracker')
        return
    }
    Write-Host 'Open http://localhost:5100, sign in, save a trip, and copy its URL.'
    [void](Read-Host 'Press Enter after saving to restart Trips')
    Stop-Process -InputObject $processes.Trips
    $processes.Trips.WaitForExit()
    $processes.Trips = Start-TestService 'Trips'
    Wait-TestService 'Trips' $processes.Trips
    Write-Host 'Trips restarted. Reload the saved trip URL and check the itinerary.'
    [void](Read-Host 'Press Enter to stop the test stack')
}
finally {
    $healthClient.Dispose()
    foreach ($process in $processes.Values) {
        if (-not $process.HasExited) { Stop-Process -InputObject $process -ErrorAction SilentlyContinue }
    }
}
