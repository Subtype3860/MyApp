using MyApp.API.Extensions;
using MyApp.API.BackgroundServices;
using MyApp.Application;
using MyApp.Application.Security;
using MyApp.Application.Storage;
using MyApp.Infrastructure;
using MyApp.API.Games;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is required.");
var jwtSection = builder.Configuration.GetRequiredSection("Jwt");
var jwtKey = jwtSection.GetValue<string>("Key");
if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException(
        "Configuration value 'Jwt:Key' must be provided and must not be empty.");
}
if (System.Text.Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "Configuration value 'Jwt:Key' must contain at least 32 bytes.");
}
var jwtOptions = new JwtOptions(
    jwtKey,
    jwtSection.GetValue<string>("Issuer")
        ?? throw new InvalidOperationException("Configuration value 'Jwt:Issuer' is required."),
    jwtSection.GetValue<string>("Audience")
        ?? throw new InvalidOperationException("Configuration value 'Jwt:Audience' is required."));
var mediaStorageSection = builder.Configuration.GetRequiredSection("MediaStorage");
var mediaStorageOptions = new MediaStorageOptions(
    mediaStorageSection.GetValue<string>("PhotoDirectory")
        ?? throw new InvalidOperationException("Configuration value 'MediaStorage:PhotoDirectory' is required."),
    mediaStorageSection.GetValue<string>("VideoDirectory")
        ?? throw new InvalidOperationException("Configuration value 'MediaStorage:VideoDirectory' is required."),
    Path.GetFullPath(
        mediaStorageSection.GetValue<string>("StagingPhotoDirectory")
            ?? throw new InvalidOperationException("Configuration value 'MediaStorage:StagingPhotoDirectory' is required."),
        builder.Environment.ContentRootPath),
    Path.GetFullPath(
        mediaStorageSection.GetValue<string>("StagingVideoDirectory")
            ?? throw new InvalidOperationException("Configuration value 'MediaStorage:StagingVideoDirectory' is required."),
        builder.Environment.ContentRootPath));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(
    connectionString,
    jwtOptions,
    mediaStorageOptions);
builder.Services.AddHostedService<StagedMediaTransferService>();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Permissions.AuxiliaryWorksView, policy => policy.RequireClaim("permission", Permissions.AuxiliaryWorksView));
    options.AddPolicy(Permissions.AuxiliaryWorksRead, policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
        context.User.HasClaim("permission", Permissions.AuxiliaryWorksView) || (context.User.HasClaim("permission", "vehicles.report") || context.User.HasClaim("permission", Permissions.ShiftReport))));
    options.AddPolicy(Permissions.TablesView, policy =>
        policy.RequireClaim("permission", Permissions.TablesView));
    options.AddPolicy(Permissions.VehiclesView, policy =>
        policy.RequireClaim("permission", Permissions.VehiclesView));
    options.AddPolicy(Permissions.RequirementsView, policy =>
        policy.RequireClaim("permission", Permissions.RequirementsView));
    options.AddPolicy(Permissions.MaintenanceView, policy =>
        policy.RequireClaim("permission", Permissions.MaintenanceView));
});
builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ITanksAccess, TanksAccess>();
builder.Services.AddSingleton<TanksTickets>();
builder.Services.AddSingleton<TanksArena>();
builder.Services.AddSingleton<TanksServer>();
builder.Services.AddHostedService<TanksTicker>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("tanks", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
var gameSockets = new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15), KeepAliveTimeout = TimeSpan.FromSeconds(15) };
foreach (var origin in builder.Configuration.GetSection("Tanks:AllowedOrigins").Get<string[]>() ?? ["https://boora.ru", "https://www.boora.ru"])
    gameSockets.AllowedOrigins.Add(origin);
if (app.Environment.IsDevelopment())
    foreach (var origin in new[] { "http://localhost:5150", "http://127.0.0.1:5150", "http://localhost:5296" }) gameSockets.AllowedOrigins.Add(origin);
app.UseWebSockets(gameSockets);

await app.Services.InitializeDatabaseAsync();

app.MapControllers();

app.Run();
