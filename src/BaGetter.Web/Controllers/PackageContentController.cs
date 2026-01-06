using System;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Authentication;
using BaGetter.Core;
using BaGetter.Core.Exceptions;
using BaGetter.Protocol.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NuGet.Versioning;

namespace BaGetter.Web;

/// <summary>
/// The Package Content resource, used to download content from packages.
/// See: https://docs.microsoft.com/nuget/api/package-base-address-resource
/// </summary>

[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public class PackageContentController : Controller
{
    private readonly IPackageContentService _content;

    public PackageContentController(IPackageContentService content)
    {
        ArgumentNullException.ThrowIfNull(content);

        _content = content;
    }

    public async Task<ActionResult<PackageVersionsResponse>> GetPackageVersionsAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            var versions = await _content.GetPackageVersionsOrNullAsync(id, cancellationToken);
            if (versions == null)
            {
                return NotFound();
            }

            return versions;
        }
        catch (PackageLicenseBlockedException ex)
        {
            Response.Headers.Add("X-Package-Block-Reason", ex.Reason);
            Response.Headers.Add("X-Package-Block-Message", ex.Message);
            return StatusCode(403, new {
                error = "Package blocked by organization due to license issue",
                message = ex.Message,
                packageId = ex.PackageId,
                packageVersion = ex.PackageVersion.ToString(),
                reason = ex.Reason
            });
        }
    }

    /// <summary>
    /// Download a specific package version.
    /// </summary>
    /// <param name="id">Package id, e.g. "BaGetter.Protocol".</param>
    /// <param name="version">Package version, e.g. "1.2.0".</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The requested package in an octet stream, or 404 not found if the package isn't found.</returns>
    public async Task<IActionResult> DownloadPackageAsync(string id, string version, CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            return NotFound();
        }

        try
        {
            var packageStream = await _content.GetPackageContentStreamOrNullAsync(id, nugetVersion, cancellationToken);
            if (packageStream == null)
            {
                return NotFound();
            }

            return File(packageStream, "application/octet-stream");
        }
        catch (PackageLicenseBlockedException ex)
        {
            Response.Headers.Add("X-Package-Block-Reason", ex.Reason);
            Response.Headers.Add("X-Package-Block-Message", ex.Message);
            return StatusCode(403, new {
                error = "Package blocked by organization due to license issue",
                message = ex.Message,
                packageId = ex.PackageId,
                packageVersion = ex.PackageVersion.ToString(),
                reason = ex.Reason
            });
        }
    }

    public async Task<IActionResult> DownloadNuspecAsync(string id, string version, CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            return NotFound();
        }

        try
        {
            var nuspecStream = await _content.GetPackageManifestStreamOrNullAsync(id, nugetVersion, cancellationToken);
            if (nuspecStream == null)
            {
                return NotFound();
            }

            return File(nuspecStream, "text/xml");
        }
        catch (PackageLicenseBlockedException ex)
        {
            Response.Headers.Add("X-Package-Block-Reason", ex.Reason);
            Response.Headers.Add("X-Package-Block-Message", ex.Message);
            return StatusCode(403, new {
                error = "Package blocked by organization due to license issue",
                message = ex.Message,
                packageId = ex.PackageId,
                packageVersion = ex.PackageVersion.ToString(),
                reason = ex.Reason
            });
        }
    }

    public async Task<IActionResult> DownloadReadmeAsync(string id, string version, CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            return NotFound();
        }

        var readmeStream = await _content.GetPackageReadmeStreamOrNullAsync(id, nugetVersion, cancellationToken);
        if (readmeStream == null)
        {
            return NotFound();
        }

        return File(readmeStream, "text/markdown");
    }

    public async Task<IActionResult> DownloadIconAsync(string id, string version, CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            return NotFound();
        }

        var iconStream = await _content.GetPackageIconStreamOrNullAsync(id, nugetVersion, cancellationToken);
        if (iconStream == null)
        {
            return NotFound();
        }

        return File(iconStream, "image/xyz");
    }
}
