FROM mcr.microsoft.com/dotnet/sdk:7.0-alpine3.17 AS build-env

WORKDIR /app
COPY Confor.csproj ./
RUN dotnet restore Confor.csproj /property:RuntimeIdentifier=linux-musl-x64 /property:Configuration=Release

RUN apk add --no-cache clang build-base zlib-dev icu-static icu-dev openssl-dev openssl-libs-static

COPY . ./
RUN dotnet publish Confor.csproj -c Release -r linux-musl-x64 -o /build --no-restore

FROM alpine:3.17

WORKDIR /build
RUN apk add --no-cache libstdc++ icu-libs openssl
COPY --from=build-env /build .
ENTRYPOINT ["/build/confor"]
