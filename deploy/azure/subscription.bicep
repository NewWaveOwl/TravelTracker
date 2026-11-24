targetScope = 'subscription'
param location string = 'canadacentral'
param resourceGroupName string = 'rg-traveltracker-demo'
param prefix string = 'traveltracker'

resource demoGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: { project: 'TravelTracker', purpose: 'coursework-demo' }
}

module infrastructure 'infrastructure.bicep' = {
  name: 'traveltracker-infrastructure'
  scope: demoGroup
  params: { location: location, prefix: prefix }
}

output registryName string = infrastructure.outputs.registryName
output registryServer string = infrastructure.outputs.registryServer
output environmentName string = infrastructure.outputs.environmentName
output pullIdentityName string = infrastructure.outputs.pullIdentityName
