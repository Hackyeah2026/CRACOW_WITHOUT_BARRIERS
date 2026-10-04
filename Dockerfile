FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
# Project files first: package restore is cached until a dependency changes.
COPY src/Domain/Domain.csproj src/Domain/
COPY src/Application/Application.csproj src/Application/
COPY src/Infrastructure/Infrastructure.csproj src/Infrastructure/
COPY src/Infrastructure.Mongo/Infrastructure.Mongo.csproj src/Infrastructure.Mongo/
COPY src/Web.Client/Web.Client.csproj src/Web.Client/
COPY src/Web/Web.csproj src/Web/
RUN dotnet restore src/Web/Web.csproj
COPY src/ src/
# The host serves the browser (WebAssembly) client itself, so one publish covers both.
# No --no-restore: the pack with _framework/blazor.web.js is only restored by publish.
RUN dotnet publish src/Web/Web.csproj -c Release -p:DebugType=none -p:DebugSymbols=false -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Sign-in cookie keys live here; a named volume mounted on it inherits this ownership.
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown -R app:app /home/app
USER $APP_UID
ENV HOME=/home/app \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "Web.dll"]
