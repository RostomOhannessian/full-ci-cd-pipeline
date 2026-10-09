// The composition root. Endpoints, authentication, and dependency registration arrive in WP1.10.
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

await app.RunAsync();
