var builder = DistributedApplication.CreateBuilder(args);

// Explicit, stable password: without it Aspire generates a new one each run, but the data volume
// keeps the original baked in — "password authentication failed for user 'postgres'" after the
// first restart. Set via user-secrets/env "Parameters:db-password"; generated once if absent.
var dbPassword = builder.AddParameter("db-password", secret: true);

var postgres = builder.AddPostgres("postgres", password: dbPassword)
    .WithDataVolume("automatex-postgres-data");

var db = postgres.AddDatabase("automatex");

var api = builder.AddProject<Projects.AutomateX>("api")
    .WithReference(db)
    .WaitFor(db)
    // Dev convenience: `dotnet publish … -o …/plugins/<Name>` hot-reloads automatically.
    .WithEnvironment("Engine__WatchPlugins", "true")
    .WithExternalHttpEndpoints();

builder.AddViteApp("web", "../web")
    .WithPnpm()
    .WithReference(api)
    .WaitFor(api)
    // Stable dev port — webhook URLs and bookmarks survive restarts.
    .WithEndpoint("http", endpoint => endpoint.Port = 5173)
    .WithExternalHttpEndpoints();

builder.Build().Run();
