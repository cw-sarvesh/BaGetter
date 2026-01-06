using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Versioning;

namespace BaGetter.Core;

/// <summary>
/// Service to check if a package's license is blocked before downloading.
/// </summary>
public class LicenseFilterService : ILicenseFilterService
{
    private readonly IUpstreamClient _upstream;
    private readonly MirrorOptions _options;
    private readonly ILogger<LicenseFilterService> _logger;

    public LicenseFilterService(
        IUpstreamClient upstream,
        IOptions<MirrorOptions> options,
        ILogger<LicenseFilterService> logger)
    {
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> IsLicenseBlockedAsync(string packageId, NuGetVersion version, CancellationToken cancellationToken)
    {
        // If no license filters are configured, allow all packages
        if ((_options.BlockedLicenses == null || _options.BlockedLicenses.Count == 0) &&
            (_options.BlockedLicenseUrlPatterns == null || _options.BlockedLicenseUrlPatterns.Count == 0))
        {
            return false;
        }

        try
        {
            // Get license information for the specific version
            var licenseInfo = await _upstream.GetLicenseInfoOrNullAsync(packageId, version, cancellationToken);

            if (licenseInfo == null)
            {
                // If we can't get license info, log a warning but don't block (fail open)
                _logger.LogWarning(
                    "Could not retrieve license information for package {PackageId} {PackageVersion} to check license. Allowing download.",
                    packageId,
                    version);
                return false;
            }

            // Check blocked license expressions
            if (_options.BlockedLicenses != null && _options.BlockedLicenses.Count > 0)
            {
                if (!string.IsNullOrEmpty(licenseInfo.LicenseExpression))
                {
                    var licenseExpression = licenseInfo.LicenseExpression.ToLowerInvariant();
                    foreach (var blockedLicense in _options.BlockedLicenses)
                    {
                        var blockedLower = blockedLicense.ToLowerInvariant();

                        // Check if the license expression contains the blocked license
                        // This handles cases like "AGPL-3.0-only", "AGPL-3.0-or-later", etc.
                        if (licenseExpression.Contains(blockedLower))
                        {
                            _logger.LogWarning(
                                "Package {PackageId} {PackageVersion} has blocked license expression: {LicenseExpression} (blocked: {BlockedLicense})",
                                packageId,
                                version,
                                licenseInfo.LicenseExpression,
                                blockedLicense);
                            return true;
                        }
                    }
                }
            }

            // Check blocked license URL patterns
            if (_options.BlockedLicenseUrlPatterns != null && _options.BlockedLicenseUrlPatterns.Count > 0)
            {
                if (!string.IsNullOrEmpty(licenseInfo.LicenseUrl))
                {
                    foreach (var pattern in _options.BlockedLicenseUrlPatterns)
                    {
                        if (MatchesPattern(licenseInfo.LicenseUrl, pattern))
                        {
                            _logger.LogWarning(
                                "Package {PackageId} {PackageVersion} has blocked license URL pattern: {LicenseUrl} (matched pattern: {Pattern})",
                                packageId,
                                version,
                                licenseInfo.LicenseUrl,
                                pattern);
                            return true;
                        }
                    }
                }
            }

            return false;
        }
        catch (Exception e)
        {
            // If license check fails, log error but don't block (fail open)
            _logger.LogError(
                e,
                "Error checking license for package {PackageId} {PackageVersion}. Allowing download.",
                packageId,
                version);
            return false;
        }
    }

    private static bool MatchesPattern(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        // Simple wildcard matching: * matches any sequence of characters
        var patternLower = pattern.ToLowerInvariant();
        var textLower = text.ToLowerInvariant();

        if (patternLower == "*")
        {
            return true;
        }

        if (!patternLower.Contains('*'))
        {
            return textLower.Contains(patternLower);
        }

        // Convert wildcard pattern to regex-like matching
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(patternLower)
            .Replace("\\*", ".*") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(textLower, regexPattern);
    }
}

