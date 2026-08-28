#!/usr/bin/env bash
set -Eeuo pipefail

secret_file=/run/secrets/sql_sa_password
if [[ ! -s "$secret_file" ]]; then
  echo "SQL Server SA password secret is missing or empty." >&2
  exit 1
fi

export MSSQL_SA_PASSWORD
MSSQL_SA_PASSWORD="$(<"$secret_file")"
exec /opt/mssql/bin/sqlservr
