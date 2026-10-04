using FluentAssertions;

namespace HVO.WebSite.ApiTests.SqlServer;

[TestClass]
public sealed class SqlServerFixtureSafetyTests
{
    [TestMethod]
    [DataRow(null, null, null)]
    [DataRow("production.example,1433", "throwaway", "0123456789abcdef0123456789abcdef")]
    [DataRow("127.0.0.1,1433;Database=production", "throwaway", "0123456789abcdef0123456789abcdef")]
    [DataRow("127.0.0.1,1433", "throwaway", "unowned")]
    [DataRow("127.0.0.1,1433", null, "0123456789abcdef0123456789abcdef")]
    public void UnprovisionedOrRemoteConfiguration_IsRejectedBeforeConnecting(string? server, string? password, string? runId)
    {
        var validate = () => SqlServerDatabase.ValidateConfiguration(server, password, runId);
        validate.Should().Throw<InvalidOperationException>().WithMessage("*run-sql-server-integration-tests.sh*");
    }
}
