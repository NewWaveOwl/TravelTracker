#requires -Version 7.2
# Deterministic checks of the deployment's service-token boundary; no Azure or Supabase calls.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$path = Join-Path $PSScriptRoot '../deploy/azure/Deploy-Demo.ps1'
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$errors)
if ($errors.Count) { throw 'Deployment script has syntax errors.' }
# Load only these helper declarations; never execute deployment statements in tests.
foreach ($definition in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    if ($definition.Name -in @('ConvertFrom-JwtPart', 'Get-ServiceSession')) {
        . ([scriptblock]::Create($definition.Extent.Text))
    }
}
function Read-PrivateValue { param($Prompt) return 'fixture-password' }
function Encode-Part { param([byte[]]$Bytes) return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') }
$rsa = [Security.Cryptography.RSA]::Create(2048)
$ec = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
$config = [pscustomobject]@{SupabaseUrl='https://project.supabase.test'; PublishableKey='sb_publishable_fixture'; ServiceEmail='service@example.test'}
$now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$passed = 0
try {
    foreach ($scenario in @('rsa-valid', 'ec-valid', 'bad-signature', 'wrong-issuer', 'wrong-audience', 'expired', 'future-nbf', 'wrong-scope', 'extra-scope', 'anonymous', 'wrong-role', 'invalid-subject', 'unconfirmed', 'identity-mismatch', 'provider-failure')) {
        $claims = @{iss='https://project.supabase.test/auth/v1'; aud='authenticated'; exp=$now+3600; sub='971b6b85-3393-4af0-a9af-bee91b8c0b5f'; role='authenticated'; scope='users.access'; is_anonymous=$false}
        switch ($scenario) {
            'wrong-issuer' { $claims.iss='https://other.supabase.test/auth/v1' }
            'wrong-audience' { $claims.aud='other' }
            'expired' { $claims.exp=$now-60 }
            'future-nbf' { $claims.nbf=$now+3600 }
            'wrong-scope' { $claims.scope='users.write' }
            'extra-scope' { $claims.scope='users.access users.admin' }
            'anonymous' { $claims.is_anonymous=$true }
            'wrong-role' { $claims.role='anon' }
            'invalid-subject' { $claims.sub='not-an-id' }
        }
        $algorithm = if ($scenario -eq 'ec-valid') { 'ES256' } else { 'RS256' }
        $header = @{alg=$algorithm; kid='fixture-key'} | ConvertTo-Json -Compress
        $signing = (Encode-Part ([Text.Encoding]::UTF8.GetBytes($header))) + '.' + (Encode-Part ([Text.Encoding]::UTF8.GetBytes(($claims | ConvertTo-Json -Compress))))
        $inputBytes = [Text.Encoding]::ASCII.GetBytes($signing)
        if ($algorithm -eq 'RS256') {
            $public = $rsa.ExportParameters($false)
            $signature = $rsa.SignData($inputBytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
            $jwk = @{kid='fixture-key'; alg='RS256'; kty='RSA'; use='sig'; n=(Encode-Part $public.Modulus); e=(Encode-Part $public.Exponent)}
        } else {
            $public = $ec.ExportParameters($false)
            $signature = $ec.SignData($inputBytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
            $jwk = @{kid='fixture-key'; alg='ES256'; kty='EC'; use='sig'; crv='P-256'; x=(Encode-Part $public.Q.X); y=(Encode-Part $public.Q.Y)}
        }
        if ($scenario -eq 'bad-signature') { $signature[0] = $signature[0] -bxor 1 }
        $fixtureToken = $signing + '.' + (Encode-Part $signature)
        function Invoke-RestMethod {
            param([string]$Uri, $Method, $Headers, $ContentType, $Body, $MaximumRedirection, $TimeoutSec)
            if ($scenario -eq 'provider-failure') { throw 'fixture provider down' }
            if ($Uri.Contains('/token?')) { return [pscustomobject]@{access_token=$fixtureToken} }
            if ($Uri.EndsWith('/jwks.json')) { return [pscustomobject]@{keys=@([pscustomobject]$jwk)} }
            if ($Uri.EndsWith('/user')) {
                if ($Headers.Authorization -cne "Bearer $fixtureToken") { throw 'Missing bearer token.' }
                return [pscustomobject]@{id=$(if ($scenario -eq 'identity-mismatch') {'a5ec73c5-bebd-4d15-a837-2f117e090b93'} else {$claims.sub}); email_confirmed_at=$(if ($scenario -eq 'unconfirmed') {$null} else {'2026-01-01T00:00:00Z'})}
            }
            throw 'Unexpected provider endpoint.'
        }
        $accepted = $false
        try {
            $session = Get-ServiceSession $config
            if ($session.Token -cne $fixtureToken -or $session.Subject -cne $claims.sub) { throw 'Wrong returned session.' }
            $accepted=$true
        } catch {
            if ($scenario -in @('rsa-valid', 'ec-valid')) { throw }
        }
        if ($accepted -ne ($scenario -in @('rsa-valid', 'ec-valid'))) { throw "Unexpected acceptance result: $scenario" }
        Write-Host "PASS $scenario"
        $passed++
    }
} finally { $rsa.Dispose(); $ec.Dispose() }
Write-Host "$passed passed, 0 failed."
