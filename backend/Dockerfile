# Build the project
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build-env
WORKDIR /nzb-webdav
COPY ./ ./
RUN dotnet restore
RUN dotnet publish -c Release -r linux-musl-x64 -o ./publish

# Build the production image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
EXPOSE 8080
WORKDIR /nzb-webdav
RUN mkdir /config
RUN apk add --no-cache libc6-compat shadow su-exec && rm -rf /var/cache/apk/*
COPY --from=build-env /nzb-webdav/publish ./
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh
CMD ["/entrypoint.sh"]