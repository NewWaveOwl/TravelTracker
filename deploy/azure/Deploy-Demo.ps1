#requires -Version 7.2
<#
.SYNOPSIS
Builds and deploys the single-replica Azure demo, or refreshes its expiring service token.
.DESCRIPTION
Run in your own terminal. Passwords are prompted privately and are never arguments.
Infrastructure must already have been provisioned from subscription.bicep.
#>
[CmdletBinding()]
param(
    [string]$ResourceGroup = 'rg-traveltracker-demo',
    [string]$Prefix = 'traveltracker',
    [string]$Subscription = 'Azure for Students',
    [Parameter(Mandatory)][uri]$RoutingUrl,
    [ValidatePattern('^[a-zA-Z0-9_][a-zA-Z0-9_.-]{0,127}$')][string]$ImageTag,
    [switch]$RefreshOnly,
    [ValidateSet(0, 1)][int]$ApiMinReplicas = 1,
    [switch]$ApplyMigrations
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$azureCommand = Get-Command az -ErrorAction SilentlyContinue
$az = if ($azureCommand) { $azureCommand.Source } else { $null }
if (!$az -and $IsWindows) { $az = 'C:/Program Files/Microsoft SDKs/Azure/CLI2/wbin/az.cmd' }
if (!$az -or !(Test-Path $az)) { throw 'Install Azure CLI and sign in first.' }
if ($RoutingUrl.Scheme -ne 'https' -or $RoutingUrl.UserInfo -or $RoutingUrl.Query -or $RoutingUrl.Fragment) {
    throw 'RoutingUrl must be the credential-free HTTPS base address of the Windows routing API.'
}
if ($Prefix -cnotmatch '^[a-z][a-z0-9-]{2,15}$') { throw 'Prefix must be 3-16 lowercase letters, digits or hyphens, beginning with a letter.' }

function Invoke-Azure {
    param([string[]]$Arguments)
    $result = & $az @Arguments --only-show-errors
    if ($LASTEXITCODE -ne 0) { throw 'Azure operation failed. Stop and inspect the resource deployment status.' }
    return $result
}

function Read-PrivateValue {
    param([string]$Prompt)
    $secureValue = Read-Host $Prompt -AsSecureString
    try { return [System.Net.NetworkCredential]::new('', $secureValue).Password }
    finally { $secureValue.Dispose() }
}

function ConvertFrom-JwtPart {
    param([string]$Part)
    $encoded = $Part.Replace('-', '+').Replace('_', '/')
    return ,([Convert]::FromBase64String($encoded.PadRight([int]([Math]::Ceiling($encoded.Length / 4.0) * 4), '=')))
}

function Get-ServiceSession {
    param($Config)
    $origin = ([uri]$Config.SupabaseUrl).GetLeftPart([UriPartial]::Authority)
    if ($origin -cne $Config.SupabaseUrl.TrimEnd('/') -or !($origin.StartsWith('https://'))) {
        throw 'The Supabase URL must be a trusted HTTPS origin.'
    }
    if ($Config.PublishableKey -notmatch '^sb_publishable_' -and $Config.PublishableKey -notmatch '^eyJ') {
        throw 'A publishable or legacy anon key is required, never a secret key.'
    }
    if ($Config.PublishableKey -match '^eyJ') {
        $keyClaims = [Text.Encoding]::UTF8.GetString((ConvertFrom-JwtPart $Config.PublishableKey.Split('.')[1])) | ConvertFrom-Json -AsHashtable
        if ($keyClaims.role -cne 'anon') { throw 'Only a legacy anon key is allowed.' }
    }
    $headers = @{apikey = $Config.PublishableKey}
    $password = Read-PrivateValue 'Supabase dedicated Auth SERVICE ACCOUNT password (not database password)'
    try {
        $body = @{email = $Config.ServiceEmail; password = $password} | ConvertTo-Json -Compress
        try {
            $session = Invoke-RestMethod "$origin/auth/v1/token?grant_type=password" -Method Post -Headers $headers -ContentType 'application/json' -Body $body -MaximumRedirection 0 -TimeoutSec 20
            $keys = Invoke-RestMethod "$origin/auth/v1/.well-known/jwks.json" -Headers $headers -MaximumRedirection 0 -TimeoutSec 20
        } catch { throw 'Supabase service sign-in or signing-key retrieval failed. No provider response body is logged.' }
    } finally { $password = $null; $body = $null }
    $parts = $session.access_token.Split('.')
    if ($parts.Count -ne 3) { throw 'Service sign-in did not return a JWT.' }
    $header = [Text.Encoding]::UTF8.GetString((ConvertFrom-JwtPart $parts[0])) | ConvertFrom-Json -AsHashtable
    $claims = [Text.Encoding]::UTF8.GetString((ConvertFrom-JwtPart $parts[1])) | ConvertFrom-Json -AsHashtable
    $matching = @($keys.keys | Where-Object { $_.kid -ceq $header.kid })
    if ($matching.Count -ne 1 -or $header.alg -cnotin @('ES256', 'RS256')) { throw 'Service JWT must use one matching asymmetric signing key.' }
    $key = $matching[0] | ConvertTo-Json | ConvertFrom-Json -AsHashtable
    if (($key.ContainsKey('alg') -and $key.alg -cne $header.alg) -or ($key.ContainsKey('use') -and $key.use -cne 'sig')) { throw 'Signing-key purpose or algorithm mismatch.' }
    $data = [Text.Encoding]::ASCII.GetBytes($parts[0] + '.' + $parts[1])
    $signature = ConvertFrom-JwtPart $parts[2]
    $valid = $false
    if ($header.alg -ceq 'RS256' -and $key.kty -ceq 'RSA') {
        $verifier = [Security.Cryptography.RSA]::Create()
        try {
            $parameters = [Security.Cryptography.RSAParameters]::new()
            $parameters.Modulus = ConvertFrom-JwtPart $key.n
            $parameters.Exponent = ConvertFrom-JwtPart $key.e
            $verifier.ImportParameters($parameters)
            $valid = $verifier.VerifyData($data, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        } finally { $verifier.Dispose() }
    } elseif ($header.alg -ceq 'ES256' -and $key.kty -ceq 'EC' -and $key.crv -ceq 'P-256') {
        $verifier = [Security.Cryptography.ECDsa]::Create()
        try {
            $parameters = [Security.Cryptography.ECParameters]::new()
            $parameters.Curve = [Security.Cryptography.ECCurve+NamedCurves]::nistP256
            $point = [Security.Cryptography.ECPoint]::new()
            $point.X = ConvertFrom-JwtPart $key.x
            $point.Y = ConvertFrom-JwtPart $key.y
            $parameters.Q = $point
            $verifier.ImportParameters($parameters)
            $valid = $verifier.VerifyData($data, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
        } finally { $verifier.Dispose() }
    }
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $subject = [guid]::Empty
    if (!$valid -or $claims.iss -cne "$origin/auth/v1" -or @($claims.aud) -cnotcontains 'authenticated' -or
        $claims.role -cne 'authenticated' -or $claims.scope -cne 'users.access' -or
        ![guid]::TryParse($claims.sub, [ref]$subject) -or $subject -eq [guid]::Empty -or
        $claims.exp -le ($now + 600) -or ($claims.ContainsKey('nbf') -and $claims.nbf -gt $now) -or
        ($claims.ContainsKey('is_anonymous') -and $claims.is_anonymous)) { throw 'Service JWT signature, identity, permissions, issuer, audience or lifetime is invalid.' }
    try {
        $identity = Invoke-RestMethod "$origin/auth/v1/user" -Headers @{apikey=$Config.PublishableKey; Authorization="Bearer $($session.access_token)"} -MaximumRedirection 0 -TimeoutSec 20
    } catch { throw 'Supabase could not verify the service session.' }
    if ($identity.id -cne $claims.sub -or !$identity.email_confirmed_at) { throw 'The service user must be confirmed and match the verified JWT.' }
    return @{Token = $session.access_token; Subject = $claims.sub; Expires = [DateTimeOffset]::FromUnixTimeSeconds($claims.exp)}
}

Push-Location $repo
$privateDirectory = $null
try {
    Invoke-Azure @('account', 'set', '--subscription', $Subscription) | Out-Null
    $infrastructure = (Invoke-Azure @('deployment', 'group', 'show', '--resource-group', $ResourceGroup, '--name', 'traveltracker-infrastructure', '--query', 'properties.outputs', '--output', 'json')) | ConvertFrom-Json
    $config = Get-Content '.traveltracker-launch.json' -Raw | ConvertFrom-Json
    $registry = $infrastructure.registryName.value
    $server = $infrastructure.registryServer.value
    $subscriptionId = Invoke-Azure @('account', 'show', '--query', 'id', '--output', 'tsv')
    $environmentUrl = "https://management.azure.com/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.App/managedEnvironments/$($infrastructure.environmentName.value)?api-version=2026-07-01"
    $environmentStatus = (Invoke-Azure @('rest', '--method', 'get', '--url', $environmentUrl, '--query', '{mode:properties.environmentMode,state:properties.provisioningState}', '--output', 'json')) | ConvertFrom-Json
    if ($environmentStatus.mode -cne 'WorkloadProfiles') {
        throw 'This deployment requires a Standard WorkloadProfiles environment. Deploy the corrected infrastructure template before entering credentials.'
    }
    if ($environmentStatus.state -cne 'Succeeded') { throw 'The Standard environment is not ready yet. Wait for infrastructure provisioning to succeed before entering credentials.' }
    $statePath = Join-Path $repo '.artifacts/azure/demo-state.json'
    if ($RefreshOnly) {
        if ($ImageTag) { throw 'Use either -RefreshOnly or -ImageTag, not both.' }
        $imageTag = (Get-Content $statePath -Raw | ConvertFrom-Json).imageTag
    } elseif ($ImageTag) {
        $imageTag = $ImageTag
        foreach ($service in @('Users', 'Attractions', 'Transport', 'Accommodations', 'Settings', 'Trips', 'Frontend')) {
            Invoke-Azure @('acr', 'repository', 'show', '--name', $registry, '--image', "traveltracker-$($service.ToLowerInvariant()):$imageTag", '--query', 'digest', '--output', 'none') | Out-Null
        }
    } else {
        $imageTag = (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
        $login = (Invoke-Azure @('acr', 'login', '--name', $registry, '--expose-token', '--output', 'json')) | ConvertFrom-Json
        $oldUser = $env:DOTNET_CONTAINER_REGISTRY_UNAME
        $oldPassword = $env:DOTNET_CONTAINER_REGISTRY_PWORD
        try {
            $env:DOTNET_CONTAINER_REGISTRY_UNAME = '00000000-0000-0000-0000-000000000000'
            $env:DOTNET_CONTAINER_REGISTRY_PWORD = $login.accessToken
            foreach ($service in @('Users', 'Attractions', 'Transport', 'Accommodations', 'Settings', 'Trips', 'Frontend')) {
                $project = if ($service -eq 'Frontend') { 'TravelTracker.Frontend' } else { "TravelTracker.$service.Api" }
                dotnet publish "src/$project/$project.csproj" --configuration Release --os linux --arch x64 -t:PublishContainer "-p:ContainerRegistry=$server" "-p:ContainerRepository=traveltracker-$($service.ToLowerInvariant())" "-p:ContainerImageTag=$imageTag" '-p:ContainerUser=app' '-p:ContainerBaseImage=mcr.microsoft.com/dotnet/aspnet:10.0'
                if ($LASTEXITCODE -ne 0) { throw "Container publishing failed for $service." }
            }
        } finally { $env:DOTNET_CONTAINER_REGISTRY_UNAME = $oldUser; $env:DOTNET_CONTAINER_REGISTRY_PWORD = $oldPassword; $login = $null }
    }
    $serviceSession = Get-ServiceSession $config
    $databasePassword = Read-PrivateValue 'Supabase PostgreSQL DATABASE password'
    $connections = @{}
    try {
        foreach ($service in @('Users', 'Attractions', 'Transport', 'Accommodations', 'Settings', 'Trips')) {
            $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
            $connection['Host'] = $config.PoolerHost
            $connection['Port'] = '5432'
            $connection['Username'] = $config.DatabaseUser
            $connection['Password'] = $databasePassword
            $connection['Database'] = 'traveltracker_' + $service.ToLowerInvariant()
            $connection['SSL Mode'] = 'VerifyFull'
            $connection['Root Certificate'] = '/app/certs/supabase.crt'
            $connection['Maximum Pool Size'] = '5'
            $connections[$service] = $connection.ConnectionString
        }
    } finally { $databasePassword = $null }
    $parameters = @{
        prefix=$Prefix; registryName=$registry; environmentName=$infrastructure.environmentName.value
        pullIdentityName=$infrastructure.pullIdentityName.value; imageTag=$imageTag
        supabaseUrl=$config.SupabaseUrl.TrimEnd('/'); publishableKey=$config.PublishableKey
        databaseConnections=$connections; databaseCertificate=[IO.File]::ReadAllText($config.RootCertificate)
        serviceAccessToken=$serviceSession.Token; serviceSubject=$serviceSession.Subject
        routingUrl=$RoutingUrl.AbsoluteUri.TrimEnd('/'); applyMigrations=[bool]$ApplyMigrations
        apiMinReplicas=$ApiMinReplicas
    }
    # A private ephemeral parameter file avoids secrets in shell arguments and source control.
    $privateDirectory = Join-Path $repo ('.artifacts/azure/private-' + [guid]::NewGuid().ToString('N'))
    New-Item $privateDirectory -ItemType Directory -Force | Out-Null
    if ($IsWindows) {
        $acl = Get-Acl $privateDirectory
        $acl.SetAccessRuleProtection($true, $false)
        $rule = [Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.WindowsIdentity]::GetCurrent().Name, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
        Set-Acl $privateDirectory $acl
    } else { chmod 700 $privateDirectory; if ($LASTEXITCODE -ne 0) { throw 'Could not restrict temporary parameter directory.' } }
    $wrapped = @{}
    foreach ($entry in $parameters.GetEnumerator()) { $wrapped[$entry.Key] = @{value=$entry.Value} }
    $parameterPath = Join-Path $privateDirectory 'parameters.json'
    @{parameters=$wrapped} | ConvertTo-Json -Depth 12 | Set-Content $parameterPath -Encoding utf8
    Invoke-Azure @('deployment', 'group', 'create', '--resource-group', $ResourceGroup, '--name', 'traveltracker-apps', '--template-file', 'deploy/azure/apps.bicep', '--parameters', "@$parameterPath", '--output', 'none') | Out-Null
    @{imageTag=$imageTag; tokenExpires=$serviceSession.Expires.ToString('O')} | ConvertTo-Json | Set-Content $statePath
    $website = Invoke-Azure @('deployment', 'group', 'show', '--resource-group', $ResourceGroup, '--name', 'traveltracker-apps', '--query', 'properties.outputs.websiteUrl.value', '--output', 'tsv')
    Write-Host "Website: $website"
    Write-Host "Service token expires $($serviceSession.Expires.ToString('u')). Run with -RefreshOnly before that time."
} finally {
    $parameters=$null; $wrapped=$null; $connections=$null; $serviceSession=$null
    if ($privateDirectory -and (Test-Path -LiteralPath $privateDirectory)) {
        # This exact GUID directory was created by this invocation under the repo's ignored artifacts.
        $resolvedPrivate = [IO.Path]::GetFullPath($privateDirectory)
        $expectedParent = [IO.Path]::GetFullPath((Join-Path $repo '.artifacts/azure')) + [IO.Path]::DirectorySeparatorChar
        if (!$resolvedPrivate.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($resolvedPrivate) -notmatch '^private-[0-9a-f]{32}$') { throw 'Refusing to remove an unexpected parameter directory.' }
        Remove-Item -LiteralPath $resolvedPrivate -Recurse -Force
    }
    Pop-Location
}
