using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using UniStay.Core;
namespace UniStay.Api;

public sealed class DesignTimeDb : IDesignTimeDbContextFactory<UniStayDb>
{
    public UniStayDb CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<UniStayDb>()
        .UseNpgsql("Host=localhost;Database=unistay_csharp_design;Username=design_only").Options);
}
