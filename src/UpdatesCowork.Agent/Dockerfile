FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/UpdatesCowork.Agent/UpdatesCowork.Agent.csproj -c Release -o /app/publish --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
COPY eval/fixtures/updates /app/eval-fixtures
ENV ASPNETCORE_HTTP_PORTS=8088
EXPOSE 8088
# Hosted Agent の $HOME (/home/session) は基盤が用意するため、公式サンプルと同じく既定のユーザーで動かす。
# $APP_UID にすると $HOME に成果物を書けない(UnauthorizedAccessException)。
ENTRYPOINT ["dotnet", "UpdatesCowork.Agent.dll"]
