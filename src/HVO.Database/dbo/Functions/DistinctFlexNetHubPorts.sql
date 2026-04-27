-- =============================================
-- Description:	Fasst DISTINCT for hubPort
-- =============================================
CREATE FUNCTION [dbo].[DistinctFlexNetHubPorts] 
(	
)
RETURNS TABLE 
AS
RETURN 
(
  WITH RECURSIVECTE AS 
  (
    SELECT MIN(hubPort) AS hubPort FROM [OutbackMateFlexNetRecords_NEW]
    UNION all

    SELECT 
	  RANKED.hubPort
    FROM 
	(
      SELECT 
	      X.hubPort, 
		  row_number() over (order by X.hubPort) as R
      FROM 
	      [OutbackMateFlexNetRecords_NEW] X JOIN RECURSIVECTE R ON R.hubPort < X.hubPort
    ) RANKED
    WHERE RANKED.R = 1
  )

  SELECT hubPort FROM RECURSIVECTE
)

GO

