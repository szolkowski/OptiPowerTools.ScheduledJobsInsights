# Database for the CMS 12 stack (docker-compose.cms12.yml). The CMS 13 stack builds its database from
# the Alloy submodule's Docker/db.dockerfile, whose create-db script also creates a Commerce database;
# this one creates only the CMS database and does not need the submodule.

# SQL Server 2025 is published for linux/amd64 only; pin it so the image also
# builds on arm64 hosts (Apple Silicon), where it runs under emulation.
FROM --platform=linux/amd64 mcr.microsoft.com/mssql/server:2025-latest

ENV ACCEPT_EULA=Y

USER root

WORKDIR /src
COPY ./Docker/cms12-create-db.sh ./create-db.sh
RUN chmod +x /src/create-db.sh

USER mssql

EXPOSE 1433

ENTRYPOINT /src/create-db.sh & /opt/mssql/bin/sqlservr
