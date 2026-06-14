using System.Text;
using System.Text.Json;
using AutomateX.Web;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AutomateX.Tests;

// The webhook/manual-run body seam: an empty body means "no payload", valid JSON rides through
// verbatim to become {{trigger.payload}}, and malformed JSON throws (callers turn it into a 400).
public sealed class RawJsonBodyTests
{
    private static HttpContext WithBody(string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return context;
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_or_whitespace_body_is_null(string body)
    {
        Assert.Null(await RawJsonBody.ReadAsync(WithBody(body), CancellationToken.None));
    }

    [Fact]
    public async Task Valid_json_passes_through_verbatim()
    {
        const string body = """{"name":"Eirik","count":3}""";
        Assert.Equal(body, await RawJsonBody.ReadAsync(WithBody(body), CancellationToken.None));
    }

    [Fact]
    public async Task Malformed_json_throws()
    {
        await Assert.ThrowsAnyAsync<JsonException>(
            () => RawJsonBody.ReadAsync(WithBody("{not json"), CancellationToken.None));
    }
}
