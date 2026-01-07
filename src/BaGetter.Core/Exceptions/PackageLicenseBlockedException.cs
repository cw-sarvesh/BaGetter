using System;
using NuGet.Versioning;

namespace BaGetter.Core.Exceptions;

/// <summary>
/// Exception thrown when a package is blocked due to license restrictions.
/// </summary>
public class PackageLicenseBlockedException : Exception
{
    public string PackageId { get; }
    public NuGetVersion PackageVersion { get; }
    public string Reason { get; }

    public PackageLicenseBlockedException(string packageId, NuGetVersion packageVersion, string reason)
        : base($"Package {packageId} {packageVersion} is blocked by organization due to license issue: {reason}")
    {
        PackageId = packageId;
        PackageVersion = packageVersion;
        Reason = reason;
    }
}

