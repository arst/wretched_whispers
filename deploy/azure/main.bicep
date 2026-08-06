targetScope = 'resourceGroup'

@minLength(3)
@maxLength(20)
param environmentName string
param location string = resourceGroup().location
param image string = 'mcr.microsoft.com/dotnet/samples:aspnetapp'
param deployWorkloads bool = false
param deployApplication bool = deployWorkloads
param deploymentPrincipalObjectId string
param postgresAdministratorLogin string = 'wwadmin'
@secure()
param postgresAdministratorPassword string
param postgresSkuName string = 'Standard_B1ms'
param postgresSkuTier string = 'Burstable'
param postgresStorageGb int = 32
param minReplicas int = 1
param maxReplicas int = 1
param azureOpenAiEndpoint string = ''
param azureOpenAiDeployment string = ''
param customDomainName string = ''
param customDomainCertificateId string = ''

var suffix = uniqueString(resourceGroup().id, environmentName)
var compactName = toLower(replace(environmentName, '-', ''))
var appName = '${environmentName}-server'
var jobName = '${environmentName}-migrate'
var postgresName = take('${compactName}-${suffix}', 63)
var databaseName = 'wretched_whispers'
var registryName = take('${compactName}${suffix}', 50)
var vaultName = take('${compactName}-${suffix}', 24)
var identityName = '${environmentName}-workload'
var identityResourceId = workloadIdentity.id

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${environmentName}-logs'
  location: location
  properties: {
    retentionInDays: 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

resource containerEnvironment 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: '${environmentName}-environment'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: registryName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
  }
}

resource workloadIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: vaultName
  location: location
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enablePurgeProtection: true
    softDeleteRetentionInDays: 90
  }
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, workloadIdentity.id, 'AcrPull')
  scope: registry
  properties: {
    principalId: workloadIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

resource vaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, workloadIdentity.id, 'KeyVaultSecretsUser')
  scope: vault
  properties: {
    principalId: workloadIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '4633458b-17de-408a-b874-0445c86b69e6')
  }
}

resource deploymentSecretsOfficer 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, deploymentPrincipalObjectId, 'KeyVaultSecretsOfficer')
  scope: vault
  properties: {
    principalId: deploymentPrincipalObjectId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
  }
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: postgresName
  location: location
  sku: {
    name: postgresSkuName
    tier: postgresSkuTier
  }
  properties: {
    administratorLogin: postgresAdministratorLogin
    administratorLoginPassword: postgresAdministratorPassword
    version: '17'
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    network: {
      publicNetworkAccess: 'Enabled'
    }
    storage: {
      storageSizeGB: postgresStorageGb
      autoGrow: 'Enabled'
    }
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

// Required by the consumption environment's dynamic outbound addresses. Prefer private networking
// when the environment is moved into a managed VNet.
resource allowAzureServices 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: postgres
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource app 'Microsoft.App/containerApps@2025-01-01' = if (deployApplication) {
  name: appName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityResourceId}': {}
    }
  }
  properties: {
    environmentId: containerEnvironment.id
    configuration: {
      // Single-revision mode: az containerapp update does the rollout natively — new revision, health
// gate, traffic cutover, old revision deactivated. The deactivation is load-bearing: TurnWorker
// polls a shared queue from every live replica, so a lingering old revision (as blue/green keeps
// for rollback) would keep claiming turns with outdated code. Rollback = redeploy the previous
// immutable SHA-tagged image.
activeRevisionsMode: 'Single'
      maxInactiveRevisions: 5
      registries: [
        {
          server: registry.properties.loginServer
          identity: identityResourceId
        }
      ]
      secrets: [
        {
          name: 'app-db'
          keyVaultUrl: '${vault.properties.vaultUri}secrets/app-db-connection'
          identity: identityResourceId
        }
        {
          name: 'azure-openai-key'
          keyVaultUrl: '${vault.properties.vaultUri}secrets/azure-openai-key'
          identity: identityResourceId
        }
      ]
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
        customDomains: empty(customDomainName) ? [] : [
          {
            name: customDomainName
            bindingType: 'SniEnabled'
            certificateId: customDomainCertificateId
          }
        ]
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
    }
    template: {
      containers: [
        {
          name: 'server'
          image: image
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'WW_DB_PROVIDER', value: 'postgres' }
            { name: 'WW_DB_CONNECTION', secretRef: 'app-db' }
            { name: 'AzureOpenAiSettings__Endpoint', value: azureOpenAiEndpoint }
            { name: 'AzureOpenAiSettings__ChatModelDeployment', value: azureOpenAiDeployment }
            { name: 'AzureOpenAiSettings__ApiKey', secretRef: 'azure-openai-key' }
          ]
          probes: [
            {
              type: 'Startup'
              httpGet: { path: '/health/live', port: 8080, scheme: 'HTTP' }
              initialDelaySeconds: 2
              periodSeconds: 3
              failureThreshold: 20
            }
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: 8080, scheme: 'HTTP' }
              periodSeconds: 15
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: 8080, scheme: 'HTTP' }
              periodSeconds: 5
            }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
  dependsOn: [acrPull, vaultSecretsUser]
}

resource migrationJob 'Microsoft.App/jobs@2025-01-01' = if (deployWorkloads) {
  name: jobName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityResourceId}': {}
    }
  }
  properties: {
    environmentId: containerEnvironment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 900
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: identityResourceId
        }
      ]
      secrets: [
        {
          name: 'migration-db'
          keyVaultUrl: '${vault.properties.vaultUri}secrets/migration-db-connection'
          identity: identityResourceId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: image
          command: ['dotnet', 'WretchedWhispers.Migrations.dll']
          env: [
            { name: 'WW_DB_PROVIDER', value: 'postgres' }
            { name: 'WW_DB_CONNECTION', secretRef: 'migration-db' }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
    }
  }
  dependsOn: [acrPull, vaultSecretsUser]
}

output containerAppName string = appName
output migrationJobName string = jobName
output registryName string = registry.name
output registryLoginServer string = registry.properties.loginServer
output keyVaultName string = vault.name
output postgresServerName string = postgres.name
output postgresHost string = postgres.properties.fullyQualifiedDomainName
output databaseName string = database.name
output managedIdentityId string = workloadIdentity.id
output publicFqdn string = deployApplication ? app!.properties.configuration.ingress.fqdn : ''
output greenFqdn string = deployApplication
  ? replace(app!.properties.configuration.ingress.fqdn, '${appName}.', '${appName}---green.')
  : ''
