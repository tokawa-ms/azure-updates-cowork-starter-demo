// Azure Updates Cowork のインフラ(10 章)。
// azd と「Deploy to Azure」ボタンの両方から使う。リソースグループ単位でデプロイする。
//
// Foundry は 2 通り。
// - existingAiProjectResourceId を指定: 既存のプロジェクトとモデルデプロイを使う(新しく作らない)。
// - 空: Foundry アカウント、プロジェクト、モデルデプロイをこのリソースグループに作る。

targetScope = 'resourceGroup'

@minLength(1)
@maxLength(20)
@description('環境名。リソース名の一部になる(英小文字、数字、ハイフン)')
param environmentName string

@description('リソースの場所')
param location string = resourceGroup().location

@description('開発者のオブジェクト ID。ローカル実行に必要なロールを付ける。空なら付けない')
param principalId string = ''

@allowed(['User', 'ServicePrincipal'])
param principalType string = 'User'

@description('Hosted Agent の名前')
param agentName string = 'updates-cowork'

@description('既存の Foundry プロジェクトのリソース ID。空なら新しく作る')
param existingAiProjectResourceId string = ''

@description('既存の Application Insights のリソース ID。空なら新しく作る。既存のプロジェクトを使う場合は、そのプロジェクトに接続済みのものを指定する')
param existingAppInsightsResourceId string = ''

@description('エージェントが使うモデル')
param agentModelName string = 'gpt-5.4-mini'
param agentModelVersion string = '2026-03-17'
param agentModelCapacity int = 30

@description('エージェントが使うモデルデプロイの名前。空ならモデル名と同じ')
param agentDeploymentName string = ''

@description('評価の判定に使うモデル')
param judgeModelName string = 'gpt-5-mini'
param judgeModelVersion string = '2025-08-07'
param judgeModelCapacity int = 30

@description('評価の判定に使うモデルデプロイの名前。空ならモデル名と同じ')
param judgeDeploymentName string = ''

@description('エージェント用の RAI ポリシー(ガードレール)を作るか')
param createRaiPolicy bool = true

@description('RAI ポリシーの名前')
param raiPolicyName string = 'auc-agent-guardrail'

@description('開発者に Foundry のロールを付けるか。既存のプロジェクトで既に同じロールを持っている場合は false にする')
param assignDeveloperFoundryRoles bool = true

@description('web と siteproxy に接続を許す送信元の CIDR。空なら制限しない。例: ["203.0.113.10/32"]')
param allowedIpRanges array = []

@allowed(['Anonymous', 'EntraId'])
param authMode string = 'Anonymous'

@description('アラートの通知先。空ならアラートを作らない')
param alertEmail string = ''

@minValue(0)
@maxValue(10)
param siteproxyMinReplicas int = 0

@description('旧リバースプロキシを構築する場合だけ true。既存リソースは false にしても削除しない')
param enableLegacyProxy bool = false

@minValue(1)
@maxValue(10)
param siteproxyMaxReplicas int = 3

@description('azd が設定する。true ならデプロイ済みのイメージを使い続ける')
param webExists bool = false
param siteproxyExists bool = false

@description('web のイメージ。azd を使わないとき(Deploy to Azure ボタン)に指定する')
param webImage string = ''

@description('siteproxy のイメージ。azd を使わないとき(Deploy to Azure ボタン)に指定する')
param siteproxyImage string = ''

var resourceToken = toLower(uniqueString(subscription().id, resourceGroup().id, environmentName))
var tags = {
  'azd-env-name': environmentName
  application: 'azure-updates-cowork'
}
var compactEnvName = toLower(replace(environmentName, '-', ''))

var useExistingProject = !empty(existingAiProjectResourceId)
var useExistingAppInsights = !empty(existingAppInsightsResourceId)
var projectIdParts = split(existingAiProjectResourceId, '/')
var aiSubscriptionId = useExistingProject ? projectIdParts[2] : subscription().subscriptionId
var aiResourceGroupName = useExistingProject ? projectIdParts[4] : resourceGroup().name
var aiAccountName = useExistingProject ? projectIdParts[8] : 'aif-${compactEnvName}-${take(resourceToken, 8)}'
var aiProjectName = useExistingProject ? projectIdParts[10] : 'proj-${environmentName}'
var effectiveAgentDeploymentName = empty(agentDeploymentName) ? agentModelName : agentDeploymentName
var effectiveJudgeDeploymentName = empty(judgeDeploymentName) ? judgeModelName : judgeDeploymentName

var webAppName = 'ca-web-${environmentName}'
var siteproxyAppName = 'ca-proxy-${environmentName}'

// ---- 監視 ----

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    location: location
    tags: tags
    logAnalyticsName: 'log-${environmentName}'
    appInsightsName: 'appi-${environmentName}'
    createAppInsights: !useExistingAppInsights
  }
}

var appInsightsIdParts = split(existingAppInsightsResourceId, '/')

resource existingAppInsights 'Microsoft.Insights/components@2020-02-02' existing = if (useExistingAppInsights) {
  name: appInsightsIdParts[8]
  scope: resourceGroup(appInsightsIdParts[2], appInsightsIdParts[4])
}

resource newAppInsights 'Microsoft.Insights/components@2020-02-02' existing = if (!useExistingAppInsights) {
  name: 'appi-${environmentName}'
}

var appInsightsId = useExistingAppInsights ? existingAppInsights.id : monitoring.outputs.appInsightsId
var appInsightsConnectionString = useExistingAppInsights
  ? existingAppInsights!.properties.ConnectionString
  : newAppInsights!.properties.ConnectionString

// ---- ID ----

resource webIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-web-${environmentName}'
  location: location
  tags: tags
}

resource siteproxyIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = if (enableLegacyProxy) {
  name: 'id-proxy-${environmentName}'
  location: location
  tags: tags
}

// ---- Foundry ----

module foundryNew 'modules/foundry-new.bicep' = if (!useExistingProject) {
  name: 'foundry-new'
  params: {
    location: location
    tags: tags
    accountName: aiAccountName
    projectName: aiProjectName
    agentDeploymentName: effectiveAgentDeploymentName
    agentModelName: agentModelName
    agentModelVersion: agentModelVersion
    agentModelCapacity: agentModelCapacity
    judgeDeploymentName: effectiveJudgeDeploymentName
    judgeModelName: judgeModelName
    judgeModelVersion: judgeModelVersion
    judgeModelCapacity: judgeModelCapacity
    appInsightsId: appInsightsId
    appInsightsConnectionString: appInsightsConnectionString
  }
}

module foundry 'modules/foundry-access.bicep' = {
  name: 'auc-foundry-${environmentName}'
  scope: resourceGroup(aiSubscriptionId, aiResourceGroupName)
  params: {
    accountName: aiAccountName
    projectName: aiProjectName
    createRaiPolicy: createRaiPolicy
    raiPolicyName: raiPolicyName
    webPrincipalId: webIdentity.properties.principalId
    developerPrincipalId: principalId
    developerPrincipalType: principalType
    assignDeveloperRoles: assignDeveloperFoundryRoles
  }
  dependsOn: [foundryNew]
}

// ---- レジストリ ----

module registry 'modules/registry.bicep' = {
  name: 'registry'
  params: {
    location: location
    tags: tags
    name: 'cr${take(compactEnvName, 20)}${resourceToken}'
    pullPrincipalIds: concat([
      webIdentity.properties.principalId
      foundry.outputs.projectPrincipalId
    ], enableLegacyProxy ? [siteproxyIdentity!.properties.principalId] : [])
    developerPrincipalId: principalId
    developerPrincipalType: principalType
  }
}

// ---- Container Apps (Express) ----

module containerEnvironment 'modules/container-apps-environment.bicep' = {
  name: 'container-apps-environment'
  params: {
    location: location
    tags: tags
    name: 'cae-${environmentName}'
    logAnalyticsName: monitoring.outputs.logAnalyticsName
  }
}

var webUrl = 'https://${webAppName}.${containerEnvironment.outputs.defaultDomain}'
var siteproxyUrl = 'https://${siteproxyAppName}.${containerEnvironment.outputs.defaultDomain}'
var agentBaseUrl = '${foundry.outputs.projectEndpoint}/agents/${agentName}/endpoint/protocols/openai'

module web 'modules/container-app.bicep' = {
  name: 'web'
  params: {
    location: location
    tags: tags
    name: webAppName
    serviceName: 'web'
    environmentId: containerEnvironment.outputs.id
    identityId: webIdentity.id
    identityClientId: webIdentity.properties.clientId
    registryServer: registry.outputs.loginServer
    appInsightsConnectionString: appInsightsConnectionString
    exists: webExists
    image: webImage
    allowedIpRanges: allowedIpRanges
    minReplicas: 1
    maxReplicas: 1
    env: [
      { name: 'Agent__Mode', value: 'Foundry' }
      { name: 'Agent__BaseUrl', value: agentBaseUrl }
      { name: 'Agent__ProjectEndpoint', value: foundry.outputs.projectEndpoint }
      { name: 'Agent__AgentName', value: agentName }
      { name: 'Artifacts__Mode', value: 'SessionFiles' }
      { name: 'Auth__Mode', value: authMode }
    ]
  }
}

module siteproxy 'modules/container-app.bicep' = if (enableLegacyProxy) {
  name: 'siteproxy'
  params: {
    location: location
    tags: tags
    name: siteproxyAppName
    serviceName: 'siteproxy'
    environmentId: containerEnvironment.outputs.id
    identityId: siteproxyIdentity!.id
    identityClientId: siteproxyIdentity!.properties.clientId
    registryServer: registry.outputs.loginServer
    appInsightsConnectionString: appInsightsConnectionString
    exists: siteproxyExists
    image: siteproxyImage
    allowedIpRanges: allowedIpRanges
    minReplicas: siteproxyMinReplicas
    maxReplicas: siteproxyMaxReplicas
    env: [
      { name: 'SiteProxy__AllowedParentOrigins__0', value: webUrl }
    ]
  }
}

// ---- アラート(任意) ----

module alerts 'modules/alerts.bicep' = if (!empty(alertEmail)) {
  name: 'alerts'
  params: {
    location: location
    tags: tags
    environmentName: environmentName
    appInsightsId: appInsightsId
    alertEmail: alertEmail
  }
}

// ---- 出力(azd の環境変数になる) ----

output AZURE_LOCATION string = location
output AZURE_RESOURCE_GROUP string = resourceGroup().name
output AZURE_AI_PROJECT_ID string = foundry.outputs.projectId
output AZURE_AI_PROJECT_ENDPOINT string = foundry.outputs.projectEndpoint
output FOUNDRY_PROJECT_ENDPOINT string = foundry.outputs.projectEndpoint
output AZURE_AI_PROJECT_NAME string = foundry.outputs.projectName
output AZURE_AI_ACCOUNT_NAME string = foundry.outputs.accountName
output AZURE_AI_ACCOUNT_ID string = foundry.outputs.accountId
output AZURE_AI_ACCOUNT_ENDPOINT string = foundry.outputs.accountEndpoint
output AZURE_AI_MODEL_DEPLOYMENT_NAME string = effectiveAgentDeploymentName
output AZURE_AI_JUDGE_DEPLOYMENT_NAME string = effectiveJudgeDeploymentName
output AZURE_AI_RAI_POLICY_ID string = foundry.outputs.raiPolicyId
output AZURE_AI_AGENT_NAME string = agentName
output AZURE_AI_EVAL_AGENT_NAME string = '${agentName}-eval'
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = registry.outputs.loginServer
output AZURE_CONTAINER_REGISTRY_RESOURCE_ID string = registry.outputs.id
output AZURE_CONTAINER_REGISTRY_NAME string = registry.outputs.name
output AZURE_CONTAINER_APPS_ENVIRONMENT_NAME string = containerEnvironment.outputs.name
output APPLICATIONINSIGHTS_CONNECTION_STRING string = appInsightsConnectionString
output APPLICATIONINSIGHTS_ID string = appInsightsId
output WEB_APP_NAME string = web.outputs.name
output SITEPROXY_APP_NAME string = enableLegacyProxy ? siteproxy!.outputs.name : ''
output WEB_URL string = web.outputs.uri
output SITEPROXY_URL string = enableLegacyProxy ? siteproxy!.outputs.uri : ''
output WEB_URL_EXPECTED string = webUrl
output SITEPROXY_URL_EXPECTED string = enableLegacyProxy ? siteproxyUrl : ''
