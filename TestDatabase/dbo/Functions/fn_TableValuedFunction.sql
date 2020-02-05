CREATE FUNCTION [dbo].[fn_TableValuedFunction] ()
RETURNS @returntable TABLE
(
	c1 datetime primary key,
	c2 datetime2(7)
)
AS
BEGIN
	INSERT @returntable
	SELECT GETDATE(), SYSDATETIME()
	RETURN
END
