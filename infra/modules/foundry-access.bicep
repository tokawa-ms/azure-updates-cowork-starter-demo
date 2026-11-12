// Foundry プロジェクト(新規でも既存でも)に対する追加の設定。
// Foundry のリソースグループをスコープにして呼ぶ。
// - エージェント用の RAI ポリシー(ガードレール)を作る
// - web のマネージド ID に Foundry Agent Consumer を付ける
// - 開発者に Foundry Project Manager と Foundry User を付ける(任意)
// 既存のリソースは変更せず、子リソースとロールの割り当てを足すだけにする。

param accountName string
param projectName string

param createRaiPolicy bool = true
param raiPolicyName string = 'auc-agent-guardrail'

@description('web のマネージド ID のプリンシパル ID')
param webPrincipalId string

@description('開発者のオブジェクト ID。空ならロールを付けない')
param developerPrincipalId string = ''

@allowed(['User', 'ServicePrincipal'])
param developerPrincipalType string = 'User'

@description('開発者に Foundry のロールを付けるか。既存のプロジェクトで同じロールを持っている場合は false にする')
param assignDeveloperRoles bool = true

var roles = {
  agentConsumer: 'eed3b665-ab3a-47b6-8f48-c9382fb1dad6'
  projectManager: 'eadc314b-1a2d-4efa-be10-5d325db5065e'
  foundryUser: '53ca6127-db72-4b80-b1b0-d745d6d5456d'
}

var harmCategories = ['Hate', 'Sexual', 'Violence', 'Selfharm']
var harmFilters = flatten(map(harmCategories, c => [
  { name: c, severityThreshold: 'Medium', blocking: true, enabled: true, source: 'Prompt' }
  { name: c, severityThreshold: 'Medium', blocking: true, enabled: true, source: 'Completion' }
]))

resource account 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: accountName
}

resource project 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' existing = {
  parent: account
  name: projectName
}

// Microsoft.DefaultV2 を基準に、Jailbreak と Indirect Attack を有効にしたガードレール
resource raiPolicy 'Microsoft.CognitiveServices/accounts/raiPolicies@2025-06-01' = if (createRaiPolicy) {
  parent: account
  name: raiPolicyName
  properties: {
    mode: 'Blocking'
    basePolicyName: 'Microsoft.DefaultV2'
    contentFilters: concat(harmFilters, [
      { name: 'Jailbreak', blocking: true, enabled: true, source: 'Prompt' }
      { name: 'Indirect Attack', blocking: true, enabled: true, source: 'Prompt' }
      { name: 'Protected Material Text', blocking: true, enabled: true, source: 'Completion' }
      { name: 'Protected Material Code', blocking: false, enabled: true, source: 'Completion' }
    ])
  }
}

resource webAgentConsumer 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: project
  name: guid(project.id, webPrincipalId, roles.agentConsumer)
  properties: {
    principalId: webPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.agentConsumer)
  }
}

resource developerProjectManager 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (assignDeveloperRoles && !empty(developerPrincipalId)) {
  scope: project
  name: guid(project.id, developerPrincipalId, roles.projectManager)
  properties: {
    principalId: developerPrincipalId
    principalType: developerPrincipalType
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.projectManager)
  }
}

resource developerFoundryUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (assignDeveloperRoles && !empty(developerPrincipalId)) {
  scope: project
  name: guid(project.id, developerPrincipalId, roles.foundryUser)
  properties: {
    principalId: developerPrincipalId
    principalType: developerPrincipalType
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.foundryUser)
  }
}

output accountId string = account.id
output accountName string = account.name
output accountEndpoint string = account.properties.endpoint
output projectId string = project.id
output projectName string = project.name
output projectEndpoint string = project.properties.endpoints['AI Foundry API']
output projectPrincipalId string = project.identity.principalId
output raiPolicyId string = createRaiPolicy ? raiPolicy.id : ''
