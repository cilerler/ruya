--! Ruya MessageQueue tests - Create the isolated integration-test database.
--! Parameters:
--!   @p0 (SYSNAME) - Test database identifier.
--!   @p1 (BIT) - Debug mode (1 prints the quoted statement; 0 executes it).
--! Security: QUOTENAME protects the dynamic database identifier.
SET NOCOUNT ON;

/*
-- DEBUG: Uncomment this block to test the script in SSMS.
DECLARE @p0 SYSNAME = N'RuyaMessageQueueTests';
DECLARE @p1 BIT = 1;
*/

DECLARE @DatabaseName SYSNAME = @p0;
DECLARE @Debug BIT = COALESCE(@p1, 0);

IF NULLIF(@DatabaseName, N'') IS NULL
    THROW 51201, 'DatabaseName is required.', 1;
IF EXISTS
(
    SELECT 1
    FROM sys.databases AS database_info
    WHERE database_info.[name] = @DatabaseName
)
    THROW 51209, 'The requested test database already exists.', 1;

DECLARE @Sql NVARCHAR(MAX) = N'CREATE DATABASE ' + QUOTENAME(@DatabaseName) + N';';
IF @Debug = 1
BEGIN
    PRINT @Sql;
    RETURN;
END;

EXEC sys.sp_executesql @Sql;
