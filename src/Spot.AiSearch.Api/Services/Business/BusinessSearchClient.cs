using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Spot.AiSearch.Api.DTOs;
using Spot.AiSearch.Api.Options;
using Spot.Shared.Pagination;

namespace Spot.AiSearch.Api.Services.Business;

/// <inheritdoc cref="IBusinessSearchClient" />
public sealed class BusinessSearchClient(
    HttpClient httpClient,
    IOptions<BusinessApiOptions> options,
    ILogger<BusinessSearchClient> logger) : IBusinessSearchClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<PaginatedResponse<BusinessDto>> SearchAsync(BusinessSearchRequest request, CancellationToken ct = default)
    {
        var businessOptions = options.Value;
        var requestUri = QueryHelpers.AddQueryString(
            $"{businessOptions.BaseUrl.TrimEnd('/')}/business/businesses", BuildQuery(request));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(businessOptions.TimeoutSeconds));

        try
        {
            using var response = await httpClient.GetAsync(requestUri, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Business search returned {StatusCode}.", (int)response.StatusCode);
                throw new BusinessSearchUnavailableException($"Business search returned {(int)response.StatusCode}.");
            }

            return await response.Content.ReadFromJsonAsync<PaginatedResponse<BusinessDto>>(SerializerOptions, timeoutCts.Token)
                ?? throw new BusinessSearchUnavailableException("Business search returned an empty body.");
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Business search timed out after {TimeoutSeconds}s.", businessOptions.TimeoutSeconds);
            throw new BusinessSearchUnavailableException(
                $"Business search timed out after {businessOptions.TimeoutSeconds}s.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Business search call failed.");
            throw new BusinessSearchUnavailableException("Business search call failed.", ex);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Business search returned a body that isn't a business page.");
            throw new BusinessSearchUnavailableException("Business search returned an unreadable body.", ex);
        }
    }

    private static Dictionary<string, string?> BuildQuery(BusinessSearchRequest request)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = request.Page.ToString(CultureInfo.InvariantCulture),
            ["pageSize"] = request.PageSize.ToString(CultureInfo.InvariantCulture),
        };

        if (request.Q is { } q)
            query["q"] = q;

        if (request.CategoryId is { } categoryId)
            query["categoryId"] = categoryId.ToString();

        // Business answers a partial trio with 400, so proximity goes all together or not at all.
        if (request is { Latitude: { } lat, Longitude: { } lng, RadiusKm: { } radiusKm })
        {
            query["lat"] = lat.ToString(CultureInfo.InvariantCulture);
            query["lng"] = lng.ToString(CultureInfo.InvariantCulture);
            query["radiusKm"] = radiusKm.ToString(CultureInfo.InvariantCulture);
        }

        return query;
    }
}
