--! Ruya MessageQueue - Check whether Service Broker is enabled for the current database.
--! Parameters:
--!   @p0 (BIT) - Debug mode (1 prints the operation without querying; 0 executes it).
--! Security: Reads database metadata only and does not accept object identifiers.
SET NOCOUNT ON;

/*
-- DEBUG: Uncomment this block to test the script in SSMS.
DECLARE @p0 BIT = 1;
*/

DECLARE @Debug BIT = COALESCE(@p0, 0);

IF @Debug = 1
BEGIN
    PRINT N'Ruya MessageQueue would inspect Service Broker state for the current database.';
    RETURN;
END;

SELECT database_info.[is_broker_enabled]
FROM sys.databases AS database_info
WHERE database_info.[name] = DB_NAME();
