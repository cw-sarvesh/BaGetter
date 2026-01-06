using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGetter.Core;

/// <summary>
/// Service to check if a package's license is blocked before downloading.
/// </summary>
public interface ILicenseFilterService
{
    /// <summary>
    /// Checks if a package's license is blocked based on the configured license filters.
    /// </summary>
    /// <param name="packageId">The package ID.</param>
    /// <param name="version">The package version.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>
    /// True if the package's license is blocked and should not be downloaded, false otherwise.
    /// </returns>
    Task<bool> IsLicenseBlockedAsync(string packageId, NuGetVersion version, CancellationToken cancellationToken);
}

