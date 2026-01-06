namespace BaGetter.Core;

/// <summary>
/// License information for a package.
/// </summary>
public class PackageLicenseInfo
{
    /// <summary>
    /// The license URL, if available.
    /// </summary>
    public string LicenseUrl { get; set; }

    /// <summary>
    /// The license expression (e.g., "MIT", "AGPL-3.0"), if available.
    /// </summary>
    public string LicenseExpression { get; set; }
}

