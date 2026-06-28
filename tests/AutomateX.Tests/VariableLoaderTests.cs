using AutomateX.Database;
using AutomateX.Engine.Security;
using AutomateX.Modules.Variables;
using AutomateX.Modules.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// Loads variables from the DB, decrypting secret values with the workspace DEK, and resolves them for
// an environment — the integration seam between the model, the cipher, and the pure resolution core.
public sealed class VariableLoaderTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    [Fact]
    public async Task Loads_plain_and_secret_variables_decrypted()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        var cipher = scope.ServiceProvider.GetRequiredService<TenantCipher>();
        var loader = scope.ServiceProvider.GetRequiredService<VariableLoader>();

        var ws = Workspace.DefaultId;
        var environment = WorkspaceEnvironment.Create(ws, $"env_{Guid.NewGuid():N}");
        dbContext.WorkspaceEnvironments.Add(environment);

        var region = Variable.Create(ws, workflowId: null, $"region_{Guid.NewGuid():N}", secret: false);
        region.Values.Add(VariableValue.Create(region.Id, environment.Id, "eu-west"));
        dbContext.Variables.Add(region);

        var token = Variable.Create(ws, workflowId: null, $"token_{Guid.NewGuid():N}", secret: true);
        var sealedValue = await cipher.EncryptAsync("s3cr3t", ws, CancellationToken.None);
        token.Values.Add(VariableValue.Create(token.Id, environment.Id, sealedValue));
        dbContext.Variables.Add(token);

        await dbContext.SaveChangesAsync();

        var (values, secrets) = await loader.LoadAsync(ws, Guid.NewGuid(), environment.Id, CancellationToken.None);

        Assert.Equal("eu-west", values[region.Name]);
        Assert.Equal("s3cr3t", values[token.Name]); // decrypted
        Assert.Contains(token.Name, secrets);
        Assert.DoesNotContain(region.Name, secrets);
    }
}
