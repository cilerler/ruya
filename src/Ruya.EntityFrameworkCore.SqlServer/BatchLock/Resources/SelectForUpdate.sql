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
DECLARE @p13 NVARCHAR(128);  -- PrimaryKeyField
DECLARE @p14 BIT;            -- ReturnPrimaryKeyOnly
DECLARE @p15 BIT;            -- PreserveModifiedAt
DECLARE @p16 BIT;            -- OmitModifiedAt
DECLARE @p17 BIT;            -- UpdateProcessStatusCode
DECLARE @p18 TINYINT;        -- ProcessStatusCodeNextValue
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
DECLARE @PreserveModifiedAt BIT = COALESCE(@p15, 0);
DECLARE @OmitModifiedAt BIT = COALESCE(@p16, 0);
DECLARE @UpdateProcessStatusCode BIT = COALESCE(@p17, 0);
DECLARE @ProcessStatusCodeNextValue TINYINT = COALESCE(@p18, NULL);

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
    SELECT TrimmedValue
    FROM (SELECT TRIM(value) AS TrimmedValue FROM STRING_SPLIT(@ExcludeFields, ',')) AS Parsed
    WHERE TrimmedValue <> '';
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

-- Check existence of all relevant columns in a single query for efficiency
DECLARE @SoftDeleteExists BIT = 0;
DECLARE @IsLockedExists BIT = 0;
DECLARE @ProcessingOrderFieldExists BIT = 0;
DECLARE @ProcessStatusCodeFieldExists BIT = 0;
DECLARE @LockStateExists BIT = 0;
DECLARE @LockTimeExists BIT = 0;
DECLARE @LockedByExists BIT = 0;
DECLARE @ModifiedAtExists BIT = 0;
DECLARE @PrimaryKeyFieldExists BIT = 0;

SELECT
    @SoftDeleteExists = MAX(CASE WHEN ColumnName = 'SoftDelete' THEN 1 ELSE 0 END),
    @IsLockedExists = MAX(CASE WHEN ColumnName = 'IsLocked' THEN 1 ELSE 0 END),
    @ProcessingOrderFieldExists = MAX(CASE WHEN ColumnName = @ProcessingOrderField THEN 1 ELSE 0 END),
    @ProcessStatusCodeFieldExists = MAX(CASE WHEN ColumnName = @ProcessStatusCodeField THEN 1 ELSE 0 END),
    @LockStateExists = MAX(CASE WHEN ColumnName = 'LockState' THEN 1 ELSE 0 END),
    @LockTimeExists = MAX(CASE WHEN ColumnName = 'LockTime' THEN 1 ELSE 0 END),
    @LockedByExists = MAX(CASE WHEN ColumnName = 'LockedBy' THEN 1 ELSE 0 END),
    @ModifiedAtExists = MAX(CASE WHEN ColumnName = 'ModifiedAt' THEN 1 ELSE 0 END),
    @PrimaryKeyFieldExists = MAX(CASE WHEN ColumnName = @PrimaryKeyField THEN 1 ELSE 0 END)
FROM @AllColumnInfo;

-- Validate primary key field exists
-- Note: Primary key is required for the CTE + JOIN pattern which enables ORDER BY support.
-- Without a unique key to join on, UPDATE TOP(n) cannot respect ordering.
IF @PrimaryKeyFieldExists = 0
    THROW 50003, 'The specified primary key field does not exist in the table', 1;

-- Build dynamic SET clause based on existing columns (embed actual values)
-- Note: CHAR(39) is single quote character, used to avoid quote-escaping hell
DECLARE @SetClauseItems TABLE (SetItem NVARCHAR(MAX));

IF @LockStateExists = 1
    INSERT INTO @SetClauseItems VALUES ('[LockState] = ' + CAST(@LockState AS NVARCHAR(3)));

IF @LockTimeExists = 1
    INSERT INTO @SetClauseItems VALUES ('[LockTime] = ' + CHAR(39) + CONVERT(NVARCHAR(50), @LockTime, 126) + CHAR(39));

IF @LockedByExists = 1
    INSERT INTO @SetClauseItems VALUES ('[LockedBy] = ' + CHAR(39) + REPLACE(@LockedBy, CHAR(39), CHAR(39) + CHAR(39)) + CHAR(39));

-- Handle ModifiedAt based on OmitModifiedAt and PreserveModifiedAt flags
-- OmitModifiedAt = 1: Do not include ModifiedAt in SET clause (allows triggers with IF NOT UPDATE(ModifiedAt) to fire)
-- PreserveModifiedAt = 1: Set ModifiedAt = ModifiedAt (prevents trigger from updating timestamp)
-- Default: Set ModifiedAt = SYSUTCDATETIME()
IF @ModifiedAtExists = 1 AND @OmitModifiedAt = 0
    INSERT INTO @SetClauseItems VALUES (
        CASE WHEN @PreserveModifiedAt = 1
            THEN '[ModifiedAt] = t.[ModifiedAt]'
            ELSE '[ModifiedAt] = SYSUTCDATETIME()'
        END
    );

DECLARE @SetClause NVARCHAR(MAX);

-- Handle explicit ProcessStatusCode update
IF @ProcessStatusCodeFieldExists = 1 AND @UpdateProcessStatusCode = 1
BEGIN
    IF @ProcessStatusCodeNextValue IS NULL
        INSERT INTO @SetClauseItems VALUES (QUOTENAME(@ProcessStatusCodeField) + ' = NULL');
    ELSE
        INSERT INTO @SetClauseItems VALUES (QUOTENAME(@ProcessStatusCodeField) + ' = ' + CAST(@ProcessStatusCodeNextValue AS VARCHAR(3)));
END

SELECT @SetClause = STRING_AGG('t.' + SetItem, ',' + CHAR(13) + CHAR(10) + '    ') FROM @SetClauseItems;

-- Flag to track if we have updatable columns
DECLARE @HasUpdatableColumns BIT = CASE WHEN @SetClause IS NOT NULL AND @SetClause <> '' THEN 1 ELSE 0 END;

-- Construct WHERE clause if not provided (using table variable approach)
IF @WhereClause IS NULL
BEGIN
    DECLARE @WhereConditions TABLE (Condition NVARCHAR(MAX));

    IF @SoftDeleteExists = 1
        INSERT INTO @WhereConditions VALUES ('[SoftDelete] = 0');

    IF @IsLockedExists = 1
        INSERT INTO @WhereConditions VALUES ('[IsLocked] = 0');

    IF @ProcessStatusCodeFieldExists = 1 AND @ProcessStatusCodeValue IS NOT NULL
        INSERT INTO @WhereConditions VALUES (QUOTENAME(@ProcessStatusCodeField) + ' = ' + CAST(@ProcessStatusCodeValue AS VARCHAR(3)));

    SELECT @WhereClause = STRING_AGG('t.' + Condition, ' AND ') FROM @WhereConditions;

    IF @WhereClause IS NULL OR @WhereClause = ''
        SET @WhereClause = '1=1';
END

-- Handle ORDER BY clause
IF @OrderByClause IS NULL
BEGIN
    -- Only add ORDER BY if ProcessingOrderField exists
    IF @ProcessingOrderFieldExists = 1
        SET @OrderByStatement = 'ORDER BY ' + QUOTENAME(@ProcessingOrderField) + ' ASC';
    -- Otherwise, don't include ORDER BY at all - let SQL Server decide
END
ELSE
BEGIN
    SET @OrderByStatement = 'ORDER BY ' + @OrderByClause;
END

-- Build column lists using efficient STRING_AGG with deterministic ordering
-- Note: timestamp/rowversion columns (system_type_id = 189) use binary(8) in temp table
-- because SQL Server doesn't allow explicit inserts into timestamp columns
SELECT
    @TempTableColumns = STRING_AGG(
        QUOTENAME(ColumnName) + ' ' +
        CASE
            WHEN system_type_id = 189 THEN 'binary(8)'  -- timestamp/rowversion
            ELSE TYPE_NAME(system_type_id) +
                CASE
                    WHEN TYPE_NAME(system_type_id) LIKE '%char%' OR TYPE_NAME(system_type_id) LIKE '%binary%'
                    THEN '(' + CASE WHEN max_length = -1 THEN 'MAX' ELSE CAST(CASE WHEN TYPE_NAME(system_type_id) LIKE 'n%' THEN max_length/2 ELSE max_length END AS NVARCHAR(10)) END + ')'
                    WHEN TYPE_NAME(system_type_id) IN ('decimal', 'numeric')
                    THEN '(' + CAST(precision AS NVARCHAR(10)) + ',' + CAST(scale AS NVARCHAR(10)) + ')'
                    WHEN TYPE_NAME(system_type_id) IN ('datetime2', 'datetimeoffset', 'time')
                    THEN '(' + CAST(scale AS NVARCHAR(10)) + ')'
                    ELSE ''
                END
        END +
        CASE WHEN is_nullable = 1 OR system_type_id = 189 THEN ' NULL' ELSE ' NOT NULL' END,
        ',' + CHAR(13) + CHAR(10) + '    '
    ) WITHIN GROUP (ORDER BY column_id),
    @OutputColumns = STRING_AGG(
        'INSERTED.' + QUOTENAME(ColumnName),
        ',' + CHAR(13) + CHAR(10) + '    '
    ) WITHIN GROUP (ORDER BY column_id),
    @InsertColumns = STRING_AGG(
        QUOTENAME(ColumnName),
        ',' + CHAR(13) + CHAR(10) + '    '
    ) WITHIN GROUP (ORDER BY column_id)
FROM @ColumnInfo;

-- Build the dynamic SQL based on whether we have updatable columns
IF @HasUpdatableColumns = 1
BEGIN
    -- Build SQL with UPDATE statement
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
    ' + @SetClause + '
OUTPUT
    ' + @OutputColumns + '
INTO @UpdatedRows (
    ' + @InsertColumns + '
)
FROM ' + QUOTENAME(@SchemaName) + '.' + QUOTENAME(@TableName) + ' t
INNER JOIN TargetBatch tb ON t.' + QUOTENAME(@PrimaryKeyField) + ' = tb.' + QUOTENAME(@PrimaryKeyField) + ';

' + CASE WHEN @ReturnPrimaryKeyOnly = 1
    THEN 'SELECT ' + QUOTENAME(@PrimaryKeyField) + ' FROM @UpdatedRows;'
    ELSE 'SELECT * FROM @UpdatedRows;'
END;
END
ELSE
BEGIN
    -- No updatable columns - cannot perform SELECT FOR UPDATE without marking rows
    THROW 50004, 'No updatable columns found. Table must have at least one of: LockState, LockTime, LockedBy, ModifiedAt. Cannot perform SELECT FOR UPDATE without a way to mark locked rows.', 1;
END

IF @Debug = 1
BEGIN
    -- Debug mode: print info and SQL without executing
    DECLARE @DebugMessage NVARCHAR(MAX) = 'DEBUG: Building SQL for table ' + QUOTENAME(@SchemaName) + '.' + QUOTENAME(@TableName);
    RAISERROR(@DebugMessage, 0, 1) WITH NOWAIT;

    -- Print SQL in chunks (RAISERROR has 2048 char limit)
    -- Find last newline within chunk to avoid cutting mid-word
    DECLARE @pos INT = 1;
    DECLARE @chunkSize INT = 2000;
    DECLARE @chunk NVARCHAR(2000);
    DECLARE @lastNewline INT;
    WHILE @pos <= LEN(@SQL)
    BEGIN
        SET @chunk = SUBSTRING(@SQL, @pos, @chunkSize);
        -- If not at the end, find last newline to break cleanly
        IF @pos + @chunkSize <= LEN(@SQL)
        BEGIN
            SET @lastNewline = @chunkSize - CHARINDEX(CHAR(10), REVERSE(@chunk)) + 1;
            IF @lastNewline > 0 AND @lastNewline < @chunkSize
            BEGIN
                SET @chunk = SUBSTRING(@SQL, @pos, @lastNewline);
                SET @pos = @pos + @lastNewline;
            END
            ELSE
                SET @pos = @pos + @chunkSize;
        END
        ELSE
            SET @pos = @pos + @chunkSize;
        RAISERROR(@chunk, 0, 1) WITH NOWAIT;
    END
END
ELSE
BEGIN
    EXEC sp_executesql @SQL;
END
