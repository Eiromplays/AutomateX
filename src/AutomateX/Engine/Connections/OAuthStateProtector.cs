using System.Text.Json;
using AutomateX.Engine.Security;

namespace AutomateX.Engine.Connections;

// The OAuth `state` payload — carried through the provider round-trip. Encrypting it makes it
// both tamper-proof (CSRF protection) and self-contained, so no server-side session/table is
// needed: the callback recovers the connection, workspace and PKCE verifier straight from it.
public sealed record OAuthStateData(Guid ConnectionId, Guid WorkspaceId, string Verifier, long IssuedAtUnix);

public sealed class OAuthStateProtector(SecretCipher cipher)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    public string Protect(OAuthStateData data) => cipher.Encrypt(JsonSerializer.Serialize(data));

    // Returns null on tamper, wrong key, malformed payload, or an expired/future-dated state.
    public OAuthStateData? Unprotect(string state)
    {
        try
        {
            var data = JsonSerializer.Deserialize<OAuthStateData>(cipher.Decrypt(state));
            if (data is null)
            {
                return null;
            }

            var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(data.IssuedAtUnix);
            return age >= TimeSpan.Zero && age <= MaxAge ? data : null;
        }
        catch (Exception ex) when (ex is SecretCipherException or JsonException or FormatException)
        {
            return null;
        }
    }
}
