--! Ruya MessageQueue - Enable Service Broker for a provisioned database.
--! Parameters:
--!   @p0 (SYSNAME) - Exact database identifier.
--!   @p1 (BIT) - Debug mode (1 prints the quoted statement without executing; 0 executes it).
--! Security: QUOTENAME protects the dynamic database identifier. Requires ALTER DATABASE.
SET NOCOUNT ON;

/*
-- DEBUG: Uncomment this block to test the script in SSMS.
DECLARE @p0 SYSNAME = DB_NAME();
DECLARE @p1 BIT = 1;
*/

DECLARE @DatabaseName SYSNAME = @p0;
DECLARE @Debug BIT = COALESCE(@p1, 0);

IF NULLIF(@DatabaseName, N'') IS NULL
    THROW 51102, 'DatabaseName is required.', 1;
IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases AS database_info
    WHERE database_info.[name] = @DatabaseName
)
    THROW 51112, 'The requested database does not exist.', 1;

DECLARE @Sql NVARCHAR(MAX) =
    N'ALTER DATABASE ' + QUOTENAME(@DatabaseName)
    + N' SET ENABLE_BROKER WITH ROLLBACK IMMEDIATE;';
IF @Debug = 1
BEGIN
    PRINT @Sql;
    RETURN;
END;

EXEC sys.sp_executesql @Sql;
