// 新しい Foundry アカウント、プロジェクト、モデルデプロイ、Application Insights の接続を作る。
// 既存のプロジェクトを使うとき(existingAiProjectResourceId を指定したとき)は呼ばれない。

param location string
param tags object
param accountName string
param projectName string

param agentDeploymentName string
param agentModelName string
param agentModelVersion string
param agentModelCapacity int

param judgeDeploymentName string
param judgeModelName string
param judgeModelVersion string
param judgeModelCapacity int

param appInsightsId string

@secure()
param appInsightsConnectionString string

var roles = {
  foundryUser: '53ca6127-db72-4b80-b1b0-d745d6d5456d'
  reader: 'acdd72a7-3385-48ef-bd42-f606fba81ae7'
}

resource account 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: accountName
  location: location
  tags: tags
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    allowProjectManagement: true
    customSubDomainName: accountName
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}

resource project 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: account
  name: projectName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    displayName: projectName
    description: 'Azure Updates Cowork のエージェントと評価'
  }
}

resource agentDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: account
  name: agentDeploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: agentModelCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: agentModelName
      version: agentModelVersion
    }
    raiPolicyName: 'Microsoft.DefaultV2'
  }
}

// 同じアカウントのデプロイは並べて作れないので、順に作る
resource judgeDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = if (judgeDeploymentName != agentDeploymentName) {
  parent: account
  name: judgeDeploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: judgeModelCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: judgeModelName
      version: judgeModelVersion
    }
    raiPolicyName: 'Microsoft.DefaultV2'
  }
  dependsOn: [agentDeployment]
}

// Hosted Agent に APPLICATIONINSIGHTS_CONNECTION_STRING を注入してもらうための接続
resource appInsightsConnection 'Microsoft.CognitiveServices/accounts/projects/connections@2025-06-01' = {
  parent: project
  name: 'appinsights'
  properties: {
    category: 'AppInsights'
    target: appInsightsId
    authType: 'ApiKey'
    isSharedToAll: false
    credentials: {
      key: appInsightsConnectionString
    }
    metadata: {
      ApiType: 'Azure'
      ResourceId: appInsightsId
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: last(split(appInsightsId, '/'))
}

// プロジェクトの ID: 継続評価のルール
resource projectFoundryUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: project
  name: guid(project.id, 'project-mi', roles.foundryUser)
  properties: {
    principalId: project.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.foundryUser)
  }
}

// プロジェクトの ID: トレースを使う評価
resource projectAppInsightsReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: appInsights
  name: guid(appInsightsId, 'project-mi', roles.reader)
  properties: {
    principalId: project.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.reader)
  }
}

output accountId string = account.id
output projectId string = project.id
