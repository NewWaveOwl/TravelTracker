param location string = resourceGroup().location
@minLength(3)
@maxLength(16)
param prefix string = 'traveltracker'

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: 'tt${uniqueString(subscription().id, resourceGroup().id)}'
  location: location
  // Matches the owner's listed Standard Registry Unit free-service allowance.
  sku: { name: 'Standard' }
  properties: { adminUserEnabled: false }
}

resource environment 'Microsoft.App/managedEnvironments@2026-07-01' = {
  // Environment mode cannot be converted in place. Use a new, explicitly Standard environment.
  name: '${prefix}-standard'
  location: location
  properties: {
    environmentMode: 'WorkloadProfiles'
    // No Log Analytics workspace: avoid an additional billed logging resource.
    workloadProfiles: [{ name: 'Consumption', workloadProfileType: 'Consumption' }]
  }
}

resource pullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${prefix}-image-pull'
  location: location
}

resource pullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, pullIdentity.id, 'AcrPull')
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: pullIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

output registryName string = registry.name
output registryServer string = registry.properties.loginServer
output environmentName string = environment.name
output pullIdentityName string = pullIdentity.name
