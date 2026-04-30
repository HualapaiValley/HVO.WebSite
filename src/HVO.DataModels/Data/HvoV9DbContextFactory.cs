using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HVO.DataModels.Data;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> to create <see cref="HvoV9DbContext"/>
/// without requiring a running application host. Uses a stub connection string that is
/// only needed for migration generation, not for execution.
/// </summary>
public class HvoV9DbContextFactory : IDesignTimeDbContextFactory<HvoV9DbContext>
{
    public HvoV9DbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HvoV9DbContext>()
            .UseSqlServer("Server=.;Database=HvoV9;Trusted_Connection=True;")
            .Options;
        return new HvoV9DbContext(options);
    }
}
