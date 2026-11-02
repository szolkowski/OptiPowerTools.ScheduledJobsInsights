#!/bin/bash

# Creates DB_NAME once SQL Server accepts connections. Runs alongside sqlservr (see
# cms12-db.dockerfile); the compose healthcheck waits for the database to exist, so the web container
# never starts against a missing catalog.

query="IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '${DB_NAME}') CREATE DATABASE [${DB_NAME}];"

echo "Creating database: ${DB_NAME}"

for i in {1..100}; do
    if /opt/mssql-tools18/bin/sqlcmd -b -S localhost -U sa -P "$SA_PASSWORD" -Q "$query" -C; then
        echo "Creating database completed"
        break
    fi
    echo "Creating database. Not ready yet..."
    sleep 1
done
