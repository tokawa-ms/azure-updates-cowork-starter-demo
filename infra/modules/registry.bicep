// web、siteproxy、エージェントのイメージを置く Container Registry。
// 管理者ユーザーは無効にし、Entra ID のトークンだけで読み書きする。

param location string
param tags object
param name string

@description('AcrPull を付けるプリンシパル ID (web、siteproxy のマネージド ID、Foundry プロジェクトの ID など)')
param pullPrincipalIds array = []

@description('開発者のオブジェクト ID。空ならロールを付けない')
param developerPrincipalId string = ''

@allowed(['User', 'ServicePrincipal'])
param developerPrincipalType string = 'User'

var roles = {
  acrPull: '7f951dda-4ed3-4680-a7ca-43fe172d538d'
  acrPush: '8311e382-0749-4cb8-b61a-304f252e45ec'
  tasksContributor: 'fb382eab-e894-4461-af04-94435c366c3f'
}

resource registry 'Microsoft.ContainerRegistry/registries@2025-04-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
    anonymousPullEnabled: false
    publicNetworkAccess: 'Enabled'
    dataEndpointEnabled: false
    encryption: {
      status: 'disabled'
    }
    policies: {
      azureADAuthenticationAsArmPolicy: {
        status: 'enabled'
      }
    }
  }
}

resource pullAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principalId in pullPrincipalIds: {
    scope: registry
    name: guid(registry.id, principalId, roles.acrPull)
    properties: {
      principalId: principalId
      principalType: 'ServicePrincipal'
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull)
    }
  }
]

// 開発者: azd のリモートビルド(ACR Tasks)とローカルビルドのプッシュ
resource developerPush 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(developerPrincipalId)) {
  scope: registry
  name: guid(registry.id, developerPrincipalId, roles.acrPush)
  properties: {
    principalId: developerPrincipalId
    principalType: developerPrincipalType
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPush)
  }
}

resource developerTasks 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(developerPrincipalId)) {
  scope: registry
  name: guid(registry.id, developerPrincipalId, roles.tasksContributor)
  properties: {
    principalId: developerPrincipalId
    principalType: developerPrincipalType
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.tasksContributor)
  }
}

output id string = registry.id
output name string = registry.name
output loginServer string = registry.properties.loginServer
