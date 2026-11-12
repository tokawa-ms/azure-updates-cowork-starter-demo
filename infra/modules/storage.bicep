// 成果物(Blob)、Data Protection の鍵(Blob)、会話の台帳(Table)を置くストレージ。
// 共有キーは無効にし、Entra ID の RBAC だけで読み書きする。

param location string
param tags object
param name string

@description('web のマネージド ID のプリンシパル ID')
param webPrincipalId string

@description('開発者のオブジェクト ID。空ならロールを付けない')
param developerPrincipalId string = ''

@allowed(['User', 'ServicePrincipal'])
param developerPrincipalType string = 'User'

@description('成果物を自動で消すまでの日数')
param artifactRetentionDays int = 7

var roles = {
  blobDataReader: '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'
  blobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  tableDataContributor: '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
}

resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: name
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    allowSharedKeyAccess: false
    allowBlobPublicAccess: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Disabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  parent: storage
  name: 'default'
}

resource artifactsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: 'artifacts'
  properties: {
    publicAccess: 'None'
  }
}

resource dataProtectionContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: 'dataprotection'
  properties: {
    publicAccess: 'None'
  }
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2024-01-01' = {
  parent: storage
  name: 'default'
}

resource ledgerTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2024-01-01' = {
  parent: tableService
  name: 'auc'
}

resource lifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2024-01-01' = {
  parent: storage
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'delete-artifacts'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: {
              blobTypes: ['blockBlob']
              prefixMatch: ['artifacts/']
            }
            actions: {
              baseBlob: {
                delete: {
                  daysAfterModificationGreaterThan: artifactRetentionDays
                }
              }
            }
          }
        }
      ]
    }
  }
}

// web: 成果物の読み出し
resource webArtifactsReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: artifactsContainer
  name: guid(artifactsContainer.id, webPrincipalId, roles.blobDataReader)
  properties: {
    principalId: webPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.blobDataReader)
  }
}

// web: Data Protection の鍵の読み書き
resource webDataProtectionContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: dataProtectionContainer
  name: guid(dataProtectionContainer.id, webPrincipalId, roles.blobDataContributor)
  properties: {
    principalId: webPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.blobDataContributor)
  }
}

// web: 会話の台帳
resource webTableContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, webPrincipalId, roles.tableDataContributor)
  properties: {
    principalId: webPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.tableDataContributor)
  }
}

// 開発者: ローカル実行と動作確認
resource developerBlobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(developerPrincipalId)) {
  scope: storage
  name: guid(storage.id, developerPrincipalId, roles.blobDataContributor)
  properties: {
    principalId: developerPrincipalId
    principalType: developerPrincipalType
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.blobDataContributor)
  }
}

resource developerTableContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(developerPrincipalId)) {
  scope: storage
  name: guid(storage.id, developerPrincipalId, roles.tableDataContributor)
  properties: {
    principalId: developerPrincipalId
    principalType: developerPrincipalType
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.tableDataContributor)
  }
}

output id string = storage.id
output name string = storage.name
output blobEndpoint string = storage.properties.primaryEndpoints.blob
output tableEndpoint string = storage.properties.primaryEndpoints.table
output artifactsContainerName string = artifactsContainer.name
output artifactsContainerId string = artifactsContainer.id
output dataProtectionBlobUri string = '${storage.properties.primaryEndpoints.blob}${dataProtectionContainer.name}/keys.xml'
output tableName string = ledgerTable.name
