// Complete-mode partner of main.bicep: declares ONLY what must survive a teardown — the GitHub
// deploy identity. Deployed with --mode Complete, ARM deletes every other resource in the group,
// while the identity, its federated credential, and the group-scoped role assignments survive,
// so the next what-if → apply → bootstrap cycle needs no portal or Cloud Shell work.
param location string = resourceGroup().location
param deployIdentityName string = 'ww-github-deploy'

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: deployIdentityName
  location: location
}
