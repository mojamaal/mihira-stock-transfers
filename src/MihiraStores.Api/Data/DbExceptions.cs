using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MihiraStores.Api.Data;

public static class DbExceptions
{
    /// <summary>True when SQL Server rejected the write because of a unique index or primary key (errors 2601/2627).</summary>
    public static bool IsUniqueViolation(this DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
