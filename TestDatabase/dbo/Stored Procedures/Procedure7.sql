CREATE PROCEDURE [dbo].[Procedure7]
	@param1 int = 0,
	@param2 int
AS BEGIN
	IF OBJECT_ID('tempdb..#test') IS NOT NULL DROP TABLE #test

	CREATE TABLE #test
	(
		c1 DATETIME PRIMARY KEY,
		c2 DATETIME2(7)
	)
	
	SELECT @param1, @param2


END

