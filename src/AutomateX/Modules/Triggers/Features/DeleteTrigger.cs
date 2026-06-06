using AutomateX.Database;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Triggers.Features;

public static class DeleteTrigger
{
    public sealed class Endpoint(AutomateXDbContext dbContext) : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Delete("triggers/{id}");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            var id = Route<Guid>("id");

            var deleted = await dbContext.Triggers
                .Where(x => x.Id == id)
                .ExecuteDeleteAsync(ct);

            if (deleted == 0)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            await Send.NoContentAsync(ct);
        }
    }
}
