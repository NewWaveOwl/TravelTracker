param location string = resourceGroup().location
param prefix string = 'traveltracker'
param registryName string
param environmentName string
param pullIdentityName string
param imageTag string
param supabaseUrl string
@secure()
param publishableKey string
@secure()
param databaseConnections object
@secure()
param databaseCertificate string
@secure()
param serviceAccessToken string = ''
param serviceEmail string
@secure()
param servicePassword string
param serviceSubject string
// Secret changes alone do not restart running replicas. Roll only the five consumers.
param serviceCredentialVersion string = utcNow('yyyyMMddHHmmss')
param routingUrl string
param applyMigrations bool = false
@allowed([0, 1])
param apiMinReplicas int = 1

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = { name: registryName }
resource environment 'Microsoft.App/managedEnvironments@2024-03-01' existing = { name: environmentName }
resource pullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = { name: pullIdentityName }

var services = ['Users', 'Attractions', 'Transport', 'Accommodations', 'Settings', 'Trips', 'Frontend']
var domain = environment.properties.defaultDomain
var usersUrl = 'https://${prefix}-users.internal.${domain}'
var frontendServices = map(take(services, 6), backend => {
  name: 'Services__${backend}'
  value: 'https://${prefix}-${toLower(backend)}.internal.${domain}'
})
var common = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
  { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
  // Ingress is the only route to the container; Azure supplies X-Forwarded-Proto.
  { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
  { name: 'Authentication__Bearer__Provider', value: 'Supabase' }
  { name: 'Authentication__Bearer__Authority', value: '${supabaseUrl}/auth/v1' }
  { name: 'Authentication__Bearer__Issuer', value: '${supabaseUrl}/auth/v1' }
  { name: 'Authentication__Bearer__Audience', value: 'authenticated' }
  { name: 'Authentication__Bearer__ScopeClaimType', value: 'scope' }
  { name: 'Authentication__Bearer__ValidAlgorithms__0', value: 'ES256' }
  { name: 'Authentication__Bearer__ValidAlgorithms__1', value: 'RS256' }
  { name: 'Supabase__Url', value: supabaseUrl }
  { name: 'Supabase__PublishableKey', secretRef: 'publishable-key' }
  { name: 'AllowedHosts', value: '*' }
]

resource apps 'Microsoft.App/containerApps@2024-03-01' = [for service in services: {
  name: '${prefix}-${toLower(service)}'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${pullIdentity.id}': {} }
  }
  properties: {
    managedEnvironmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: service == 'Frontend'
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        stickySessions: { affinity: service == 'Frontend' ? 'sticky' : 'none' }
      }
      registries: [{ server: registry.properties.loginServer, identity: pullIdentity.id }]
      secrets: concat([
        { name: 'publishable-key', value: publishableKey }
      ], service == 'Frontend' ? [] : [
        { name: 'database', value: databaseConnections[service] }
        { name: 'database-certificate', value: databaseCertificate }
      ], service == 'Frontend' || service == 'Users' ? [] : [
        { name: 'users-token', value: serviceAccessToken }
        { name: 'service-password', value: servicePassword }
      ])
    }
    template: {
      containers: [{
        name: toLower(service)
        image: '${registry.properties.loginServer}/traveltracker-${toLower(service)}:${imageTag}'
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
        env: concat(common, service == 'Frontend' ? concat([
          { name: 'Authentication__Provider', value: 'Supabase' }
          { name: 'Services__Routing', value: routingUrl }
        ], frontendServices) : [
          { name: 'ConnectionStrings__${service}', secretRef: 'database' }
          { name: 'Database__ApplyMigrations', value: string(applyMigrations) }
          { name: 'Database__SeedExamples', value: 'false' }
        ], service == 'Users' ? [
          { name: 'UsersAccess__AllowedServiceSubjects__0', value: serviceSubject }
        ] : [], contains(['Attractions', 'Transport', 'Accommodations', 'Settings'], service) ? [
          { name: 'Users__InternalBaseUrl', value: usersUrl }
          { name: 'Users__ServiceAccessToken', secretRef: 'users-token' }
        ] : [], service == 'Trips' ? [
          { name: 'TripReferences__UsersBaseUrl', value: usersUrl }
          { name: 'TripReferences__UsersServiceAccessToken', secretRef: 'users-token' }
          { name: 'TripReferences__AttractionsBaseUrl', value: 'https://${prefix}-attractions.internal.${domain}' }
          { name: 'TripReferences__AccommodationsBaseUrl', value: 'https://${prefix}-accommodations.internal.${domain}' }
          { name: 'TripReferences__TransportBaseUrl', value: 'https://${prefix}-transport.internal.${domain}' }
          // Allow backend cold starts during a demonstration.
          { name: 'TripReferences__Timeout', value: '00:00:30' }
        ] : [], service == 'Frontend' || service == 'Users' ? [] : [
          { name: 'Supabase__ServiceEmail', value: serviceEmail }
          { name: 'Supabase__ServicePassword', secretRef: 'service-password' }
          { name: 'Deployment__ServiceCredentialVersion', value: serviceCredentialVersion }
        ])
        volumeMounts: service == 'Frontend' ? [] : [{ volumeName: 'database-certificate', mountPath: '/app/certs' }]
        probes: [{
          type: 'Startup'
          tcpSocket: { port: 8080 }
          initialDelaySeconds: 5
          periodSeconds: 5
          failureThreshold: 60
        }, {
          type: 'Readiness'
          httpGet: {
            path: service == 'Frontend' ? '/' : '/health'
            port: 8080
            scheme: 'HTTP'
            httpHeaders: [{ name: 'X-Forwarded-Proto', value: 'https' }]
          }
          periodSeconds: 10
          failureThreshold: 3
        }]
      }]
      volumes: service == 'Frontend' ? [] : [{
        name: 'database-certificate'
        storageType: 'Secret'
        secrets: [{ secretRef: 'database-certificate', path: 'supabase.crt' }]
      }]
      // In-memory sessions and service ID generators currently require one replica.
      // API cold starts exceed the frontend's bounded request deadline. Keep them warm for the demo.
      scale: { minReplicas: service == 'Frontend' ? 0 : apiMinReplicas, maxReplicas: 1 }
    }
  }
}]

output websiteUrl string = 'https://${prefix}-frontend.${domain}'
