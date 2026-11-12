// Log Analytics と Application Insights。
// Log Analytics は Container Apps 環境のログ用に常に作る。
// Application Insights は既存のもの(Foundry プロジェクトに接続済みのもの)を使うときは作らない。

param location string
param tags object
param logAnalyticsName string
param appInsightsName string
param createAppInsights bool

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = if (createAppInsights) {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

output logAnalyticsId string = logAnalytics.id
output logAnalyticsName string = logAnalytics.name
output appInsightsId string = createAppInsights ? appInsights.id : ''
