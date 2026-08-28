#!/usr/bin/env bash
set -Eeuo pipefail

read_secret() {
  local path=$1
  [[ -s "$path" ]] || { echo "Required database bootstrap secret is missing: $path" >&2; exit 1; }
  tr -d '\r\n' < "$path"
}

sa_password="$(read_secret /run/secrets/sql_sa_password)"
app_password="$(read_secret /run/secrets/sql_app_password)"
migrator_password="$(read_secret /run/secrets/sql_migrator_password)"
sql_file=$(mktemp)
trap 'rm -f -- "$sql_file"' EXIT

cat > "$sql_file" <<SQL
IF DB_ID(N'NotificationServiceDb') IS NULL
BEGIN
    CREATE DATABASE [NotificationServiceDb];
END;
GO

IF SUSER_ID(N'nsa_app') IS NULL
    CREATE LOGIN [nsa_app] WITH PASSWORD = N'$app_password', CHECK_POLICY = ON;
ELSE
    ALTER LOGIN [nsa_app] WITH PASSWORD = N'$app_password';
GO

IF SUSER_ID(N'nsa_migrator') IS NULL
    CREATE LOGIN [nsa_migrator] WITH PASSWORD = N'$migrator_password', CHECK_POLICY = ON;
ELSE
    ALTER LOGIN [nsa_migrator] WITH PASSWORD = N'$migrator_password';
GO

USE [NotificationServiceDb];
GO

IF USER_ID(N'nsa_app') IS NULL CREATE USER [nsa_app] FOR LOGIN [nsa_app];
IF USER_ID(N'nsa_migrator') IS NULL CREATE USER [nsa_migrator] FOR LOGIN [nsa_migrator];
GO

IF IS_ROLEMEMBER(N'db_datareader', N'nsa_app') <> 1 ALTER ROLE [db_datareader] ADD MEMBER [nsa_app];
IF IS_ROLEMEMBER(N'db_datawriter', N'nsa_app') <> 1 ALTER ROLE [db_datawriter] ADD MEMBER [nsa_app];
GRANT EXECUTE TO [nsa_app];
IF IS_ROLEMEMBER(N'db_owner', N'nsa_migrator') <> 1 ALTER ROLE [db_owner] ADD MEMBER [nsa_migrator];
GO
SQL

export SQLCMDPASSWORD="$sa_password"
/opt/mssql-tools18/bin/sqlcmd \
  -S sqlserver,1433 \
  -U sa \
  -C \
  -b \
  -i "$sql_file" >/dev/null

echo "Database and least-privilege application identities are reconciled."
