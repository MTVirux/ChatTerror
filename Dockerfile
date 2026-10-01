FROM node:24 AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ChatTerror.Protocol/ChatTerror.Protocol.csproj src/ChatTerror.Protocol/
COPY src/ChatTerror.Server/ChatTerror.Server.csproj src/ChatTerror.Server/
RUN dotnet restore src/ChatTerror.Server/ChatTerror.Server.csproj
COPY src/ChatTerror.Protocol/ src/ChatTerror.Protocol/
COPY src/ChatTerror.Server/ src/ChatTerror.Server/
COPY --from=web /src/src/ChatTerror.Server/wwwroot/ src/ChatTerror.Server/wwwroot/
RUN dotnet publish src/ChatTerror.Server/ChatTerror.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
RUN mkdir /data && chown $APP_UID /data
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Relay__DbPath=/data/relay.db
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "ChatTerror.Server.dll"]
