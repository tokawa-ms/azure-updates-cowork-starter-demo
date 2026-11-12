# web (画面と BFF)のコンテナーイメージ。ビルドコンテキストはリポジトリのルートにする。
#   docker build -f src/UpdatesCowork.Web/Dockerfile -t auc-web:local .
# ACR のリモートビルドでも扱える Dockerfile 構文を使う。

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# 復元の層をキャッシュするため、先に共通設定とプロジェクトファイルだけをコピーする。
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/UpdatesCowork.Shared/UpdatesCowork.Shared.csproj src/UpdatesCowork.Shared/
COPY src/UpdatesCowork.AzureUpdates/UpdatesCowork.AzureUpdates.csproj src/UpdatesCowork.AzureUpdates/
COPY src/UpdatesCowork.Web/UpdatesCowork.Web.csproj src/UpdatesCowork.Web/
RUN dotnet restore src/UpdatesCowork.Web/UpdatesCowork.Web.csproj

COPY src/UpdatesCowork.Shared/ src/UpdatesCowork.Shared/
COPY src/UpdatesCowork.AzureUpdates/ src/UpdatesCowork.AzureUpdates/
COPY src/UpdatesCowork.Web/ src/UpdatesCowork.Web/
RUN dotnet publish src/UpdatesCowork.Web/UpdatesCowork.Web.csproj \
    --configuration Release --no-restore --output /app/publish -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "UpdatesCowork.Web.dll"]
