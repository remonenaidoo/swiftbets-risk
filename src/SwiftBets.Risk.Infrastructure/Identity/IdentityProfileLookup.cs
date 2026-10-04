using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SwiftBets.Risk.Application;

namespace SwiftBets.Risk.Infrastructure.Identity;

/// <summary>Reads the customer's own profile with their token; a failure means no email rule, never a failed request.</summary>
public sealed class IdentityProfileLookup(HttpClient http) : IProfileLookup
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<string?> EmailAsync(string bearerToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode ? (await response.Content.ReadFromJsonAsync<Profile>(Json, cancellationToken))?.Email : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private sealed record Profile(string? Email);
}
