var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("automatex-postgres-data");

var db = postgres.AddDatabase("automatex");

var api = builder.AddProject<Projects.AutomateX>("api")
    .WithReference(db)
    .WaitFor(db)
    .WithExternalHttpEndpoints();

builder.AddViteApp("web", "../web")
    .WithPnpm()
    .WithReference(api)
    .WaitFor(api)
    // Stable dev port — webhook URLs and bookmarks survive restarts.
    .WithEndpoint("http", endpoint => endpoint.Port = 5173)
    .WithExternalHttpEndpoints();

builder.Build().Run();
