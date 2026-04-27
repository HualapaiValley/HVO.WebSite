
CREATE VIEW [dbo].[SecurityCameraRecords2]
AS
SELECT Id, RecordDateTime, ImageType, CameraNumber, StorageLocation, CameraType
FROM  dbo.CameraRecords
WHERE (CameraType = 2)

GO

