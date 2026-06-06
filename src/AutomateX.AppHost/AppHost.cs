var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("automatex-postgres-data");

var db = postgres.AddDatabase("automatex");

builder.AddProject<Projects.AutomateX>("api")
    .WithReference(db)
    .WaitFor(db)
    .WithExternalHttpEndpoints();

builder.Build().Run();
