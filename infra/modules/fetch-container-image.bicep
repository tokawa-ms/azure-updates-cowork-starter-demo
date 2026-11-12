// azd の再プロビジョニングで、デプロイ済みのイメージをサンプル イメージに戻さないための補助。

param exists bool
param name string

resource existingApp 'Microsoft.App/containerApps@2026-07-01' existing = if (exists) {
  name: name
}

output containers array = exists ? existingApp!.properties.template.containers : []
