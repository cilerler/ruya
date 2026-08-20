--! Ruya MessageQueue tests - Create the isolated queue-operator principal used by permission tests.
--! Parameters:
--!   @p0 (SYSNAME) - Ephemeral test login and user name.
--!   @p1 (NVARCHAR(128)) - Ephemeral generated test password.
--!   @p2 (BIT) - Debug mode (1 prints safe operation metadata; 0 executes it).
--! Security: Identifiers use QUOTENAME; the password is escaped and never printed or stored in source.
SET NOCOUNT ON;

/*
-- DEBUG: Uncomment this block to test the script in SSMS.
DECLARE @p0 SYSNAME = N'RuyaQueueOperator';
DECLARE @p1 NVARCHAR(128) = N'provide-an-ephemeral-password';
DECLARE @p2 BIT = 1;
*/

DECLARE @LoginName SYSNAME = @p0;
DECLARE @Password NVARCHAR(128) = @p1;
DECLARE @Debug BIT = COALESCE(@p2, 0);

IF NULLIF(@LoginName, N'') IS NULL OR NULLIF(@Password, N'') IS NULL
    THROW 51208, 'LoginName and Password are required.', 1;

IF @Debug = 1
BEGIN
    PRINT N'Ruya MessageQueue tests would create the quoted ephemeral queue-operator principal.';
    RETURN;
END;

IF SCHEMA_ID(N'RuyaOperatorSchema') IS NULL
    EXEC(N'CREATE SCHEMA [RuyaOperatorSchema] AUTHORIZATION [dbo];');

DECLARE @Sql NVARCHAR(MAX);
IF NOT EXISTS
(
    SELECT 1
    FROM sys.server_principals AS server_principal
    WHERE server_principal.[name] = @LoginName
)
BEGIN
    SET @Sql = N'CREATE LOGIN ' + QUOTENAME(@LoginName)
        + N' WITH PASSWORD = ' + QUOTENAME(@Password, N'''') + N';';
    EXEC sys.sp_executesql @Sql;
END;

IF USER_ID(@LoginName) IS NULL
BEGIN
    SET @Sql = N'CREATE USER ' + QUOTENAME(@LoginName)
        + N' FOR LOGIN ' + QUOTENAME(@LoginName)
        + N' WITH DEFAULT_SCHEMA = [RuyaOperatorSchema];';
END
ELSE
BEGIN
    SET @Sql = N'ALTER USER ' + QUOTENAME(@LoginName)
        + N' WITH DEFAULT_SCHEMA = [RuyaOperatorSchema];';
END;
EXEC sys.sp_executesql @Sql;

SET @Sql = N'GRANT CONTROL TO ' + QUOTENAME(@LoginName) + N';';
EXEC sys.sp_executesql @Sql;
