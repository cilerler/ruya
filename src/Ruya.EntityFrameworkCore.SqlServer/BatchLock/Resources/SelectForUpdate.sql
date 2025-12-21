--! WARNING: Use with extreme caution!
--! Directly concatenating or dynamically appending strings using @WhereClause and @OrderByClause may expose this query to SQL injection attacks.
--! Ensure these parameters are validated or sanitized before use, or preferably use parameterized queries or a safe ORM alternative.

SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
SET NOCOUNT ON;

/*
-- Debug purposes only, do not delete this block!
DECLARE @p0 NVARCHAR(128);   -- SchemaName
DECLARE @p1 NVARCHAR(128);   -- TableName
DECLARE @p2 INT;             -- BatchSize
DECLARE @p3 VARCHAR(261);    -- LockedBy
DECLARE @p4 TINYINT;         -- LockState
DECLARE @p5 DATETIME2(7);    -- LockTime
DECLARE @p6 NVARCHAR(MAX);   -- ExcludeFields
DECLARE @p7 NVARCHAR(MAX);   -- WhereClause
DECLARE @p8 NVARCHAR(MAX);   -- OrderByClause
DECLARE @p9 NVARCHAR(128);   -- ProcessStatusCodeField
DECLARE @p10 TINYINT;        -- ProcessStatusCodeValue
DECLARE @p11 NVARCHAR(128);  -- ProcessingOrderField
DECLARE @p12 BIT            = 1;  -- Debug
*/

-- Initialize parameters with improved declaration style
DECLARE @SchemaName NVARCHAR(128) = COALESCE(@p0, NULL);
DECLARE @TableName NVARCHAR(128) = COALESCE(@p1, NULL);
DECLARE @BatchSize INT = COALESCE(@p2, 1);
DECLARE @LockedBy VARCHAR(261) = COALESCE(@p3, 'Unknown');
DECLARE @LockState TINYINT = COALESCE(@p4, 1);
DECLARE @LockTime DATETIME2(7) = COALESCE(@p5, SYSUTCDATETIME());
DECLARE @ExcludeFields NVARCHAR(MAX) = COALESCE(@p6, NULL);
DECLARE @WhereClause NVARCHAR(MAX) = COALESCE(@p7, NULL);
DECLARE @OrderByClause NVARCHAR(MAX) = COALESCE(@p8, NULL);
DECLARE @ProcessStatusCodeField NVARCHAR(128) = COALESCE(@p9, 'ProcessStatusCode');
DECLARE @ProcessStatusCodeValue TINYINT = COALESCE(@p10, NULL);
DECLARE @ProcessingOrderField NVARCHAR(128) = COALESCE(@p11, 'ProcessingOrder');
DECLARE @Debug BIT = COALESCE(@p12, 0);
DECLARE @PrimaryKeyField NVARCHAR(128) = COALESCE(@p13, 'Id');
DECLARE @ReturnPrimaryKeyOnly BIT = COALESCE(@p14, 0);

-- Declare variables needed later
DECLARE @TempTableColumns NVARCHAR(MAX);
DECLARE @OutputColumns NVARCHAR(MAX);
DECLARE @InsertColumns NVARCHAR(MAX);
DECLARE @SQL NVARCHAR(MAX);
DECLARE @OrderByStatement NVARCHAR(MAX) = '';

-- Validate input parameters
IF @SchemaName IS NULL OR @TableName IS NULL
    THROW 50000, 'Schema name and table name cannot be null', 1;

-- Validate schema and table existence
IF NOT EXISTS (
    SELECT 1
    FROM sys.schemas s
    JOIN sys.tables t ON s.schema_id = t.schema_id
    WHERE s.name = @SchemaName AND t.name = @TableName
)
    THROW 50001, 'The specified schema or table does not exist', 1;

-- Get ALL column information in a single query
DECLARE @AllColumnInfo TABLE (
    ColumnName NVARCHAR(128),
    column_id INT,
    system_type_id INT,
    user_type_id INT,
    max_length INT,
    precision TINYINT,
    scale TINYINT,
    is_nullable BIT
);

INSERT INTO @AllColumnInfo
SELECT
    c.name AS ColumnName,
    c.column_id,
    c.system_type_id,
    c.user_type_id,
    c.max_length,
    c.precision,
    c.scale,
    c.is_nullable
FROM sys.columns c
JOIN sys.tables t ON c.object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE s.name = @SchemaName AND t.name = @TableName
ORDER BY c.column_id;

-- Create a table to hold excluded field names
DECLARE @ExcludedFieldsTable TABLE (FieldName NVARCHAR(128));

-- Parse excluded fields if provided using STRING_SPLIT for efficiency
IF @ExcludeFields IS NOT NULL
BEGIN
    INSERT INTO @ExcludedFieldsTable (FieldName)
    SELECT TRIM(value) AS FieldName
    FROM STRING_SPLIT(@ExcludeFields, ',')
    WHERE TRIM(value) <> '';
END;

-- Create filtered column info by removing excluded fields
DECLARE @ColumnInfo TABLE (
    ColumnName NVARCHAR(128),
    column_id INT,
    system_type_id INT,
    user_type_id INT,
    max_length INT,
    precision TINYINT,
    scale TINYINT,
    is_nullable BIT
);

INSERT INTO @ColumnInfo
SELECT * FROM @AllColumnInfo
WHERE NOT EXISTS (
    SELECT 1 FROM @ExcludedFieldsTable
    WHERE FieldName = ColumnName
);

-- Check existence of required columns from the full column list
DECLARE @SoftDeleteExists BIT = CASE WHEN EXISTS (SELECT 1 FROM @AllColumnInfo WHERE ColumnName = 'SoftDelete') THEN 1 ELSE 0 END;
DECLARE @IsLockedExists BIT = CASE WHEN EXISTS (SELECT 1 FROM @AllColumnInfo WHERE ColumnName = 'IsLocked') THEN 1 ELSE 0 END;
DECLARE @ProcessingOrderFieldExists BIT = CASE WHEN @ProcessingOrderField IS NOT NULL AND EXISTS (SELECT 1 FROM @AllColumnInfo WHERE ColumnName = @ProcessingOrderField) THEN 1 ELSE 0 END;
DECLARE @ProcessStatusCodeFieldExists BIT = CASE WHEN @ProcessStatusCodeField IS NOT NULL AND EXISTS (SELECT 1 FROM @AllColumnInfo WHERE ColumnName = @ProcessStatusCodeField) THEN 1 ELSE 0 END;

-- Construct WHERE clause if not provided (using table variable approach)
IF @WhereClause IS NULL
BEGIN
    DECLARE @WhereConditions TABLE (Condition NVARCHAR(MAX));

    IF @SoftDeleteExists = 1
        INSERT INTO @WhereConditions VALUES ('t.[SoftDelete] = 0');

    IF @IsLockedExists = 1
        INSERT INTO @WhereConditions VALUES ('t.[IsLocked] = 0');

    IF @ProcessStatusCodeFieldExists = 1 AND @ProcessStatusCodeValue IS NOT NULL
        INSERT INTO @WhereConditions VALUES ('t.' + QUOTENAME(@ProcessStatusCodeField) + ' = ' + CAST(@ProcessStatusCodeValue AS VARCHAR(3)));

    SELECT @WhereClause = STRING_AGG(Condition, ' AND ') FROM @WhereConditions;

    IF @WhereClause IS NULL OR @WhereClause = ''
        SET @WhereClause = '1=1';
END

-- Handle ORDER BY clause
IF @OrderByClause IS NULL
BEGIN
    -- Only add ORDER BY if ProcessingOrderField exists
    IF @ProcessingOrderFieldExists = 1
        SET @OrderByStatement = 'ORDER BY ' + 't.' + QUOTENAME(@ProcessingOrderField) + ' ASC';
    -- Otherwise, don't include ORDER BY at all - let SQL Server decide
END
ELSE
BEGIN
    SET @OrderByStatement = 'ORDER BY ' + @OrderByClause;
END

-- Build column lists using efficient STRING_AGG
SELECT
    @TempTableColumns = STRING_AGG(
        QUOTENAME(ColumnName) + ' ' +
        TYPE_NAME(system_type_id) +
        CASE
            WHEN TYPE_NAME(system_type_id) LIKE '%char%' OR TYPE_NAME(system_type_id) LIKE '%binary%'
            THEN '(' + CASE WHEN max_length = -1 THEN 'MAX' ELSE CAST(CASE WHEN TYPE_NAME(system_type_id) LIKE 'n%' THEN max_length/2 ELSE max_length END AS NVARCHAR(10)) END + ')'
            WHEN TYPE_NAME(system_type_id) IN ('decimal', 'numeric')
            THEN '(' + CAST(precision AS NVARCHAR(10)) + ',' + CAST(scale AS NVARCHAR(10)) + ')'
            WHEN TYPE_NAME(system_type_id) IN ('datetime2', 'datetimeoffset', 'time')
            THEN '(' + CAST(scale AS NVARCHAR(10)) + ')'
            ELSE ''
        END +
        CASE WHEN is_nullable = 1 THEN ' NULL' ELSE ' NOT NULL' END,
        ',' + CHAR(13) + CHAR(10) + '    '
    ),
    @OutputColumns = STRING_AGG(
        'INSERTED.' + QUOTENAME(ColumnName),
        ',' + CHAR(13) + CHAR(10) + '    '
    ),
    @InsertColumns = STRING_AGG(
        QUOTENAME(ColumnName),
        ',' + CHAR(13) + CHAR(10) + '    '
    )
FROM @ColumnInfo;

-- Build the dynamic SQL - fixed CTE syntax
SET @SQL = '
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

DECLARE @UpdatedRows TABLE (
    ' + @TempTableColumns + '
);

WITH TargetBatch AS (
    SELECT TOP (' + CAST(@BatchSize AS NVARCHAR(10)) + ')
        t.' + QUOTENAME(@PrimaryKeyField) + '
    FROM ' + QUOTENAME(@SchemaName) + '.' + QUOTENAME(@TableName) + ' AS t WITH (ROWLOCK, UPDLOCK, READPAST)
    WHERE ' + @WhereClause;

-- Add ORDER BY only if it's specified
IF @OrderByStatement <> ''
    SET @SQL = @SQL + CHAR(13) + CHAR(10) + '    ' + @OrderByStatement;

SET @SQL = @SQL + CHAR(13) + CHAR(10) + ')
UPDATE t
SET
    t.LockState = @LockState,
    t.LockTime = @LockTime,
    t.LockedBy = @LockedBy
OUTPUT
    ' + @OutputColumns + '
INTO @UpdatedRows (
    ' + @InsertColumns + '
)
FROM ' + QUOTENAME(@SchemaName) + '.' + QUOTENAME(@TableName) + ' t
INNER JOIN TargetBatch tb ON t.' + QUOTENAME(@PrimaryKeyField) + ' = tb.' + QUOTENAME(@PrimaryKeyField) + ';

IF @ReturnPrimaryKeyOnly = 1
    SELECT ' + QUOTENAME(@PrimaryKeyField) + ' FROM @UpdatedRows;
ELSE
SELECT * FROM @UpdatedRows;';

-- For security logging and debugging
DECLARE @DebugMessage NVARCHAR(MAX) = 'Executing dynamic SQL for table ' + QUOTENAME(@SchemaName) + '.' + QUOTENAME(@TableName);
RAISERROR(@DebugMessage, 0, 1) WITH NOWAIT;

IF @Debug = 1
BEGIN
    RAISERROR(@SQL, 0, 1) WITH NOWAIT;
END
ELSE
BEGIN
    -- Execute the dynamic SQL with parameters
    EXEC sp_executesql @SQL,
        N'@BatchSize INT, @LockState TINYINT, @LockTime DATETIME2(7), @LockedBy VARCHAR(261), @ReturnPrimaryKeyOnly BIT',
        @BatchSize, @LockState, @LockTime, @LockedBy, @ReturnPrimaryKeyOnly;
END
