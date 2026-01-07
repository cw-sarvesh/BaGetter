using System;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace BaGetter.Database.MySql;

public class MySqlContext : AbstractContext<MySqlContext>
{
    private readonly DatabaseOptions _bagetterOptions;

    /// <summary>
    /// The MySQL Server error code for when a unique constraint is violated.
    /// </summary>
    private const int UniqueConstraintViolationErrorCode = 1062;

    /// <summary>
    /// The MySQL Server error code for when a table already exists.
    /// </summary>
    private const int TableAlreadyExistsErrorCode = 1050;

    public MySqlContext(DbContextOptions<MySqlContext> efOptions, IOptionsSnapshot<BaGetterOptions> bagetterOptions) : base(efOptions)
    {
        _bagetterOptions = bagetterOptions.Value.Database;
    }

    public override bool IsUniqueConstraintViolationException(DbUpdateException exception)
    {
        return exception.InnerException is MySqlException mysqlException &&
               mysqlException.Number == UniqueConstraintViolationErrorCode;
    }

    /// <summary>
    /// MySQL does not support LIMIT clauses in subqueries for certain subquery operators.
    /// See: https://dev.mysql.com/doc/refman/8.0/en/subquery-restrictions.html
    /// </summary>
    public override bool SupportsLimitInSubqueries => false;

    /// <summary>
    /// Override RunMigrationsAsync to handle cases where tables already exist,
    /// which can occur when upgrading from MySQL 8.0 to 8.4 with existing data.
    /// The normalized server version should prevent most issues, but this provides additional safety.
    /// </summary>
    public override async Task RunMigrationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await base.RunMigrationsAsync(cancellationToken);
        }
        catch (MySqlException ex) when (ex.Number == TableAlreadyExistsErrorCode)
        {
            // If tables already exist, this might indicate that:
            // 1. The database was created with MySQL 8.0 and migrations were applied
            // 2. Now connecting with MySQL 8.4, and EF Core detected "pending changes" due to version differences
            // 3. EF Core tried to create tables that already exist
            //
            // With the normalized server version, this should be rare, but we handle it gracefully.
            // Provide a more informative error message to help diagnose the issue.
            throw new InvalidOperationException(
                "Database tables already exist. This may occur when upgrading from MySQL 8.0 to 8.4. " +
                "The normalized server version should prevent this issue. If you see this error, it may indicate " +
                "that the migration history table (__EFMigrationsHistory) is out of sync. " +
                "Ensure all migrations have been applied correctly.",
                ex);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Use the latin1 charset as default instead of the utf8mb4 to prevent the "Row size too large" error.
        modelBuilder.HasCharSet("latin1");

        base.OnModelCreating(modelBuilder);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if(!optionsBuilder.IsConfigured)
        {
            // Auto-detect the server version, but normalize MySQL 8.x versions to 8.0.0
            // to ensure consistent migration behavior across MySQL 8.0, 8.1, 8.2, 8.3, and 8.4.
            // This prevents EF Core from detecting false "pending changes" when the actual
            // server version differs but the schema is compatible.
            var detectedVersion = ServerVersion.AutoDetect(_bagetterOptions.ConnectionString);
            var serverVersion = NormalizeServerVersion(detectedVersion);
            optionsBuilder.UseMySql(_bagetterOptions.ConnectionString, serverVersion);
        }
    }

    /// <summary>
    /// Normalizes the detected MySQL server version to ensure consistent behavior.
    /// MySQL 8.0 through 8.4 are backward compatible, so we normalize all 8.x versions
    /// to 8.0.0 to prevent migration detection issues while still using auto-detection.
    /// </summary>
    private static ServerVersion NormalizeServerVersion(ServerVersion detectedVersion)
    {
        // If the detected version is MySQL 8.x (8.0, 8.1, 8.2, 8.3, 8.4), normalize to 8.0.0
        // This ensures consistent migration behavior while still detecting the actual server.
        // For versions outside 8.x, use the detected version as-is.
        var versionString = detectedVersion.ToString();

        // Check if it's a MySQL 8.x version (starts with 8.)
        if (versionString.StartsWith("8.", StringComparison.OrdinalIgnoreCase))
        {
            return ServerVersion.Parse("8.0.0-mysql");
        }

        // For other versions (e.g., 5.7, 9.0+), use the detected version
        return detectedVersion;
    }
}
