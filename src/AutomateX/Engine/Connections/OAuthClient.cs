using System.Net.Http.Headers;
using System.Text.Json;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Engine.Connections;

public sealed class OAuthException(string message) : Exception(message);

// HTTP side of the OAuth2 flow: code exchange and refresh. Credentials go in the form body
// (the broadly-supported default); parsing/expiry handling lives in the pure OAuthFlow.
public sealed class OAuthClient(IHttpClientFactory httpClientFactory)
{
    public Task<OAuthTokens> ExchangeCodeAsync(
        OAuthConfig config, string redirectUri, string code, string? codeVerifier, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
        };

        if (codeVerifier is { Length: > 0 })
        {
            form["code_verifier"] = codeVerifier;
        }

        return PostAsync(config, form, cancellationToken);
    }

    public Task<OAuthTokens> RefreshAsync(OAuthConfig config, string refreshToken, CancellationToken cancellationToken) =>
        PostAsync(config, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        }, cancellationToken);

    private async Task<OAuthTokens> PostAsync(
        OAuthConfig config, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        form["client_id"] = config.ClientId;
        if (!string.IsNullOrEmpty(config.ClientSecret))
        {
            form["client_secret"] = config.ClientSecret;
        }

        var http = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, config.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new OAuthException($"Could not reach the token endpoint: {ex.Message}");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new OAuthException($"Token endpoint returned {(int)response.StatusCode}.");
            }

            try
            {
                return OAuthFlow.ParseTokenResponse(body, DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                throw new OAuthException("Token endpoint returned an unexpected response.");
            }
        }
    }
}
