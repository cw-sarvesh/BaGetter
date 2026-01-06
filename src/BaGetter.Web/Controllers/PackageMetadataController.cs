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
/// The Package Metadata resource, used to fetch packages' information.
/// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
/// </summary>

[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public class PackageMetadataController : Controller
{
    private readonly IPackageMetadataService _metadata;

    public PackageMetadataController(IPackageMetadataService metadata)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    // GET v3/registration/{id}.json
    [HttpGet]
    public async Task<ActionResult<BaGetterRegistrationIndexResponse>> RegistrationIndexAsync(string id, CancellationToken cancellationToken)
    {
        var index = await _metadata.GetRegistrationIndexOrNullAsync(id, cancellationToken);
        if (index == null)
        {
            return NotFound();
        }

        return index;
    }

    // GET v3/registration/{id}/{version}.json
    [HttpGet]
    public async Task<ActionResult<RegistrationLeafResponse>> RegistrationLeafAsync(string id, string version, CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            return NotFound();
        }

        try
        {
            var leaf = await _metadata.GetRegistrationLeafOrNullAsync(id, nugetVersion, cancellationToken);
            if (leaf == null)
            {
                return NotFound();
            }

            return leaf;
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
}
