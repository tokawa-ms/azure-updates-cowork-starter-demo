var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok());
await app.RunAsync().ConfigureAwait(false);