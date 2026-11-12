// 検索アラート(8.7 節)。alertEmail が空なら main.bicep から呼ばれない。

param location string
param tags object
param environmentName string
param appInsightsId string

@description('通知先のメールアドレス')
param alertEmail string

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-${environmentName}'
  location: 'global'
  tags: tags
  properties: {
    groupShortName: take('auc-${environmentName}', 12)
    enabled: true
    emailReceivers: [
      {
        name: 'owner'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

// 15 分間で execute_tool の失敗率が 20% を超えたとき
resource toolFailureRate 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = {
  name: 'alert-tool-failure-${environmentName}'
  location: location
  tags: tags
  properties: {
    displayName: 'Azure Updates Cowork: ツール呼び出しの失敗率'
    description: '15 分間で execute_tool の失敗率が 20% を超えました。'
    severity: 2
    enabled: true
    scopes: [appInsightsId]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      allOf: [
        {
          query: '''
dependencies
| where name startswith "execute_tool"
| summarize total = count(), failed = countif(success == false)
| extend failureRate = iff(total == 0, 0.0, todouble(failed) / total)
| where total >= 5 and failureRate > 0.2
'''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [actionGroup.id]
    }
  }
}

// 15 分間で chat.turn の 95 パーセンタイルが 120 秒を超えたとき
resource chatTurnLatency 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = {
  name: 'alert-chat-latency-${environmentName}'
  location: location
  tags: tags
  properties: {
    displayName: 'Azure Updates Cowork: 応答時間'
    description: '15 分間で chat.turn の 95 パーセンタイルが 120 秒を超えました。'
    severity: 3
    enabled: true
    scopes: [appInsightsId]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      allOf: [
        {
          query: '''
union requests, dependencies
| where name == "chat.turn"
| summarize p95 = percentile(duration, 95)
| where p95 > 120000
'''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [actionGroup.id]
    }
  }
}
