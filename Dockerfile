# One image, one origin. The API serves the built SPA from wwwroot, so there is no CORS to
# configure, no second service to keep in sync, and the PWA's relative /api calls work as
# they do locally.

FROM node:22-alpine AS frontend
WORKDIR /src/frontend
# Dependencies are copied on their own so a source-only change does not reinstall them.
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY backend/ ./backend/
RUN dotnet publish backend/Api/Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=backend /app/publish ./
COPY --from=frontend /src/frontend/dist ./wwwroot

# The database lives on a mounted volume, never inside the image — a redeploy replaces the
# image, and anything written into it would be a month of records thrown away.
ENV ConnectionStrings__Default="Data Source=/data/financeapp.db"
# Cookie-encryption keys go on the same volume: inside the container they would be lost on
# every redeploy, which looks exactly like "the password stopped working".
ENV DataProtection__KeyPath="/data/keys"
VOLUME /data

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Auth__Password is deliberately NOT defaulted here. The app refuses to start in a
# non-development environment without it, which is the point: a deployment that forgets the
# password fails loudly instead of publishing someone's finances.
ENTRYPOINT ["dotnet", "Api.dll"]
