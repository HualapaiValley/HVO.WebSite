using FluentAssertions;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class DatabaseProjectSchemaTests
{
    [TestMethod]
    public void SmartShuntDetailSnapshot_MatchesCanonicalEfMigration()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "HVO.Database",
            "v9",
            "Tables",
            "SmartShuntDetailSnapshot.sql"));

        sql.Should().Contain("CREATE TABLE [v9].[SmartShuntDetailSnapshot]");
        sql.Should().Contain("[Id]               BIGINT        IDENTITY (1, 1) NOT NULL");
        sql.Should().Contain("[SourceId]         NVARCHAR (64) NOT NULL");
        sql.Should().Contain("[SourceSystem]     NVARCHAR (64) NULL");
        sql.Should().Contain("[DeviceId]         NVARCHAR (64) NULL");
        sql.Should().Contain("[RecordedAt]       DATETIME2     NOT NULL");
        sql.Should().Contain("[ConsumedAh]       FLOAT         NULL");
        sql.Should().Contain("[RemainingMinutes] FLOAT         NULL");
        sql.Should().Contain("[StarterVoltageV]  FLOAT         NULL");
        sql.Should().Contain("[TemperatureC]     FLOAT         NULL");
        sql.Should().Contain("[CreatedAt]        DATETIME2     NOT NULL");
        sql.Should().Contain("CONSTRAINT [PK_SmartShuntDetailSnapshot] PRIMARY KEY CLUSTERED ([Id] ASC)");
        sql.Should().Contain("CREATE NONCLUSTERED INDEX [IX_SmartShuntDetailSnapshot_RecordedAt]");
        sql.Should().Contain("ON [v9].[SmartShuntDetailSnapshot] ([RecordedAt] ASC)");
        sql.Should().Contain("CREATE UNIQUE NONCLUSTERED INDEX [IX_SmartShuntDetailSnapshot_SourceId_RecordedAt]");
        sql.Should().Contain("ON [v9].[SmartShuntDetailSnapshot] ([SourceId] ASC, [RecordedAt] ASC)");
        sql.Should().NotContain("DEFAULT");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HVO.WebSite.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
