SET NOCOUNT ON;

/*
-- Debug purposes only, do not delete this block!
DECLARE @Resource NVARCHAR(255) = N'debug-lock-key';
*/

-- APPLOCK_MODE is session-sensitive. Running this query through the connection
-- that acquired the lock proves this exact SQL session still owns it.
SELECT APPLOCK_MODE(N'public', @Resource, N'Session');
