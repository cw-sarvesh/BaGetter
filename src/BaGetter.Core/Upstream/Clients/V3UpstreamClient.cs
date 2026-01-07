using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Protocol;
using BaGetter.Protocol.Catalog;
using BaGetter.Protocol.Internal;
using BaGetter.Protocol.Models;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace BaGetter.Core;

/// <summary>
/// The mirroring client for a NuGet server that uses the V3 protocol.
/// </summary>
public class V3UpstreamClient : IUpstreamClient
{
    private readonly NuGetClient _client;
    private readonly ILogger<V3UpstreamClient> _logger;
    private static readonly char[] AuthorSeparator = [',', ';', '\t', '\n', '\r'];
    private static readonly char[] TagSeparator = [' '];

    public V3UpstreamClient(NuGetClient client, ILogger<V3UpstreamClient> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _logger = logger;
    }

    public async Task<Stream> DownloadPackageOrNullAsync(
        string id,
        NuGetVersion version,
        CancellationToken cancellationToken)
    {
        try
        {
            using var downloadStream = await _client.DownloadPackageAsync(id, version, cancellationToken);
            return await downloadStream.AsTemporaryFileStreamAsync(cancellationToken);
        }
        catch (PackageNotFoundException)
        {
            return null;
        }
        catch (Exception e)
        {
            _logger.LogError(
                e,
                "Failed to download {PackageId} {PackageVersion} from upstream",
                id,
                version);
            return null;
        }
    }

    public async Task<IReadOnlyList<Package>> ListPackagesAsync(
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            var packages = await _client.GetPackageMetadataAsync(id, cancellationToken);

            return packages.Select(ToPackage).ToList();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to mirror {PackageId}'s upstream metadata", id);
            return new List<Package>();
        }
    }

    public async Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _client.ListPackageVersionsAsync(id, includeUnlisted: true, cancellationToken);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to mirror {PackageId}'s upstream versions", id);
            return new List<NuGetVersion>();
        }
    }

    public async Task<PackageLicenseInfo> GetLicenseInfoOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        try
        {
            var metadata = await _client.GetPackageMetadataAsync(id, version, cancellationToken);

            var licenseInfo = new PackageLicenseInfo
            {
                LicenseUrl = metadata.LicenseUrl
            };

            // Try to get LicenseExpression from the catalog leaf if CatalogLeafUrl is available
            if (!string.IsNullOrEmpty(metadata.CatalogLeafUrl))
            {
                try
                {
                    // CatalogLeafUrl in PackageMetadata points to the catalog leaf directly
                    // We need to create a catalog client to fetch it
                    var catalogLeafUrl = metadata.CatalogLeafUrl;

                    // Extract the catalog base URL from the catalog leaf URL
                    // Example: https://api.nuget.org/v3/catalog0/data/2015.02.01.12.30.45/package.id.1.0.0.json
                    // Base URL: https://api.nuget.org/v3/catalog0/index.json
                    var catalogBaseUrl = ExtractCatalogBaseUrl(catalogLeafUrl);
                    if (!string.IsNullOrEmpty(catalogBaseUrl))
                    {
                        // Create a raw catalog client to fetch the catalog leaf
                        // Note: In a production scenario, we should reuse HttpClient instances
                        // For now, we create a new one - this is acceptable for the license check
                        using var httpClient = new System.Net.Http.HttpClient();
                        var catalogClient = new RawCatalogClient(httpClient, catalogBaseUrl);

                        var catalogLeaf = await catalogClient.GetPackageDetailsLeafAsync(catalogLeafUrl, cancellationToken);
                        licenseInfo.LicenseExpression = catalogLeaf?.LicenseExpression;
                    }
                }
                catch (Exception e)
                {
                    // If we can't get the catalog leaf, log but continue with LicenseUrl only
                    // This is not a critical failure - we can still block based on LicenseUrl
                    _logger.LogDebug(
                        e,
                        "Could not fetch catalog leaf for {PackageId} {PackageVersion} to get LicenseExpression. URL: {CatalogLeafUrl}",
                        id,
                        version,
                        metadata.CatalogLeafUrl);
                }
            }

            return licenseInfo;
        }
        catch (PackageNotFoundException)
        {
            return null;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to get license info for package {PackageId} {PackageVersion}", id, version);
            return null;
        }
    }

    private static string ExtractCatalogBaseUrl(string catalogLeafUrl)
    {
        // Extract the catalog base URL from a catalog leaf URL
        // Example: https://api.nuget.org/v3/catalog0/data/2015.02.01.12.30.45/package.id.1.0.0.json
        // Base URL: https://api.nuget.org/v3/catalog0/index.json
        if (string.IsNullOrEmpty(catalogLeafUrl))
        {
            return null;
        }

        try
        {
            var uri = new Uri(catalogLeafUrl);
            var pathParts = uri.AbsolutePath.Split('/');

            // Find the catalog directory (e.g., "catalog0")
            var catalogIndex = Array.FindIndex(pathParts, p => p.StartsWith("catalog", StringComparison.OrdinalIgnoreCase));
            if (catalogIndex >= 0 && catalogIndex < pathParts.Length)
            {
                var catalogName = pathParts[catalogIndex];
                return $"{uri.Scheme}://{uri.Authority}/v3/{catalogName}/index.json";
            }
        }
        catch
        {
            // If URL parsing fails, return null
        }

        return null;
    }

    private Package ToPackage(PackageMetadata metadata)
    {
        var version = metadata.ParseVersion();

        return new Package
        {
            Id = metadata.PackageId,
            Version = version,
            Authors = ParseAuthors(metadata.Authors),
            Description = metadata.Description,
            Downloads = 0,
            HasReadme = false,
            IsPrerelease = version.IsPrerelease,
            Language = metadata.Language,
            Listed = metadata.IsListed(),
            MinClientVersion = metadata.MinClientVersion,
            Published = metadata.Published.UtcDateTime,
            RequireLicenseAcceptance = metadata.RequireLicenseAcceptance,
            Summary = metadata.Summary,
            Title = metadata.Title,
            IconUrl = ParseUri(metadata.IconUrl),
            LicenseUrl = ParseUri(metadata.LicenseUrl),
            ProjectUrl = ParseUri(metadata.ProjectUrl),
            PackageTypes = new List<PackageType>(),
            RepositoryUrl = null,
            RepositoryType = null,
            SemVerLevel = version.IsSemVer2 ? SemVerLevel.SemVer2 : SemVerLevel.Unknown,
            Tags = ParseTags(metadata.Tags),

            Dependencies = ToDependencies(metadata)
        };
    }

    private static Uri ParseUri(string uriString)
    {
        if (uriString == null) return null;

        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri;
    }

    private static string[] ParseAuthors(string authors)
    {
        if (string.IsNullOrEmpty(authors))
        {
            return Array.Empty<string>();
        }

        return authors.Split(AuthorSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string[] ParseTags(IEnumerable<string> tags)
    {
        if (tags is null)
        {
            return Array.Empty<string>();
        }

        return tags
            .SelectMany(t => t.Split(TagSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
    }

    private List<PackageDependency> ToDependencies(PackageMetadata package)
    {
        if ((package.DependencyGroups?.Count ?? 0) == 0)
        {
            return new List<PackageDependency>();
        }

        return package.DependencyGroups
            .SelectMany(ToDependencies)
            .ToList();
    }

    private IEnumerable<PackageDependency> ToDependencies(DependencyGroupItem group)
    {
        // BaGetter stores a dependency group with no dependencies as a package dependency
        // with no package id nor package version.
        if ((group.Dependencies?.Count ?? 0) == 0)
        {
            return new[]
            {
                new PackageDependency
                {
                    Id = null,
                    VersionRange = null,
                    TargetFramework = group.TargetFramework,
                }
            };
        }

        return group.Dependencies.Select(d => new PackageDependency
        {
            Id = d.Id,
            VersionRange = d.Range,
            TargetFramework = group.TargetFramework,
        });
    }
}
