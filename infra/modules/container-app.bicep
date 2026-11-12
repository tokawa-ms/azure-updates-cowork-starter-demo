// web と siteproxy で共用する Container App (Express 環境)。
// Express ではリビジョン サフィックス、トラフィックの重み、システム割り当て ID、HTTP/2 は使えない。

param location string
param tags object
param name string

@description('azure.yaml のサービス名。azd はこのタグでデプロイ先を探す')
param serviceName string

param environmentId string
param identityId string
param identityClientId string
param registryServer string

@secure()
param appInsightsConnectionString string

@description('azd の SERVICE_<NAME>_RESOURCE_EXISTS。true ならデプロイ済みのイメージを使い続ける')
param exists bool = false

@description('使うイメージ。空なら、デプロイ済みのイメージか、初回用のサンプル イメージを使う')
param image string = ''

@description('許可する送信元の CIDR。空なら制限しない')
param allowedIpRanges array = []

param minReplicas int = 0
param maxReplicas int = 3
param cpu string = '0.5'
param memory string = '1Gi'

@description('追加の環境変数 ({ name, value } の配列)')
param env array = []

// ASP.NET Core のサンプル。8080 で待ち受け、/healthz を持つ
var placeholderImage = 'mcr.microsoft.com/dotnet/samples:aspnetapp'

module fetchImage 'fetch-container-image.bicep' = {
  name: '${name}-image'
  params: {
    exists: exists && empty(image)
    name: name
  }
}

var deployedImage = exists && empty(image) && !empty(fetchImage.outputs.containers) ? fetchImage.outputs.containers[0].image : ''
var effectiveImage = !empty(image) ? image : (!empty(deployedImage) ? deployedImage : placeholderImage)

resource app 'Microsoft.App/containerApps@2026-07-01' = {
  name: name
  location: location
  tags: union(tags, { 'azd-service-name': serviceName })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: {
    environmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
        ipSecurityRestrictions: [
          for (cidr, i) in allowedIpRanges: {
            name: 'allow-${i}'
            action: 'Allow'
            ipAddressRange: cidr
          }
        ]
      }
      registries: [
        {
          server: registryServer
          identity: identityId
        }
      ]
      secrets: [
        {
          name: 'appinsights-connection-string'
          value: appInsightsConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          name: serviceName
          image: effectiveImage
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: concat(
            [
              { name: 'AZURE_CLIENT_ID', value: identityClientId }
              { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'appinsights-connection-string' }
              { name: 'OTEL_SERVICE_NAME', value: serviceName }
            ],
            env
          )
          probes: [
            {
              type: 'Readiness'
              httpGet: {
                path: effectiveImage == placeholderImage ? '/' : '/healthz'
                port: 8080
              }
              initialDelaySeconds: 3
              periodSeconds: 10
            }
            {
              type: 'Liveness'
              httpGet: {
                path: effectiveImage == placeholderImage ? '/' : '/healthz'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http'
            http: {
              metadata: {
                concurrentRequests: '30'
              }
            }
          }
        ]
      }
    }
  }
}

output id string = app.id
output name string = app.name
output fqdn string = app.properties.configuration.ingress.fqdn
output uri string = 'https://${app.properties.configuration.ingress.fqdn}'
