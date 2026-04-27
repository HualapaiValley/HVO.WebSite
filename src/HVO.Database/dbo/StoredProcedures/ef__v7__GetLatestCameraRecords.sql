
CREATE PROCEDURE [dbo].[ef__v7__GetLatestCameraRecords] 
  @iCameraType tinyint,
  @iImageType tinyint
AS
BEGIN
    SELECT 
	  R.*
	FROM 
	  CameraRecords R
	  INNER JOIN (
		SELECT 
		  CameraNumber, CameraType, ImageType, MAX(RecordDateTime) AS MaxDate
		FROM 
		  CameraRecords
        WHERE
	      CameraType = @iCameraType AND ImageType = @iImageType
		GROUP BY 
		  CameraNumber, CameraType, ImageType
	  ) G ON R.CameraNumber = G.CameraNumber AND R.CameraType = G.CameraType AND R.ImageType = G.ImageType AND R.RecordDateTime = G.MaxDate
	ORDER BY
	  R.CameraNumber ASC
END

GO

