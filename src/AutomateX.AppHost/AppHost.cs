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
    .WithExternalHttpEndpoints();

builder.Build().Run();
