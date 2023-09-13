ARG REPO_BUID="mcr.microsoft.com/dotnet/sdk:7.0"
ARG REPO_RUNTIME="mcr.microsoft.com/dotnet/aspnet:7.0"
ARG REPO_ROUTER="nginx:latest"
FROM $REPO_BUID AS build
WORKDIR /app

COPY . .
WORKDIR /app/DocSpace
RUN dotnet restore ASC.Web.slnf

WORKDIR /app/server/ASC.ZoomService
RUN dotnet publish --self-contained true -c Release -o out

FROM $REPO_RUNTIME AS api
LABEL vendor = "ONLYOFFICE" \
                maintainer = scensio System SIA <support@onlyoffice.com>
WORKDIR /app/ASC.ZoomService
COPY --from=build /app/server/ASC.ZoomService/out/. ./
COPY --from=build /app/server/ASC.ZoomService/out/config/. /app/onlyoffice/config/

ENTRYPOINT ["dotnet", "ASC.ZoomService.dll" ]
CMD ["--pathToConf", "/app/onlyoffice/config/"]

FROM $REPO_RUNTIME AS router
LABEL vendor = "ONLYOFFICE" \
                maintainer = scensio System SIA <support@onlyoffice.com>
WORKDIR /var/www/zoom
COPY ./client/public/. ./
COPY ./config/nginx/templates/nginx.conf.template  /etc/nginx/nginx.conf.template
COPY ./config/nginx/srcipts/prepare-nginx-router.sh /docker-entrypoint.d/prepare-nginx-proxy.sh
