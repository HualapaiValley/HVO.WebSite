
CREATE PROCEDURE [dbo].[ef__GetLatestAllSkyCameraRecords] 
  @iRecordsPerCamera int
AS
BEGIN
  SELECT 
    [Limit1].[Id] AS [Id], 
    [Limit1].[RecordDateTime] AS [RecordDateTime], 
    [Limit1].[ImageType] AS [ImageType], 
    [Limit1].[CameraNumber] AS [CameraNumber], 
    [Limit1].[StorageLocation] AS [StorageLocation]
    FROM   (SELECT DISTINCT 
        [Extent1].[ImageType] AS [ImageType], 
        [Extent1].[CameraNumber] AS [CameraNumber]
        FROM [dbo].[AllSkyCameraRecords] AS [Extent1] ) AS [Distinct1]
    CROSS APPLY  (SELECT TOP (@iRecordsPerCamera) [Project2].[Id] AS [Id], [Project2].[RecordDateTime] AS [RecordDateTime], [Project2].[ImageType] AS [ImageType], [Project2].[CameraNumber] AS [CameraNumber], [Project2].[StorageLocation] AS [StorageLocation]
        FROM ( SELECT 
            [Extent2].[Id] AS [Id], 
            [Extent2].[RecordDateTime] AS [RecordDateTime], 
            [Extent2].[ImageType] AS [ImageType], 
            [Extent2].[CameraNumber] AS [CameraNumber], 
            [Extent2].[StorageLocation] AS [StorageLocation]
            FROM [dbo].[AllSkyCameraRecords] AS [Extent2]
            WHERE ([Distinct1].[CameraNumber] = [Extent2].[CameraNumber]) AND ([Distinct1].[ImageType] = [Extent2].[ImageType])
        )  AS [Project2]
        ORDER BY [Project2].[RecordDateTime] DESC ) AS [Limit1]
END

GO

