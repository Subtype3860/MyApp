using MyApp.API.Extensions;
using MyApp.API.BackgroundServices;
using MyApp.Application;
using MyApp.Application.Security;
using MyApp.Application.Storage;
using MyApp.Infrastructure;

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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

await app.Services.InitializeDatabaseAsync();

app.MapControllers();

app.Run();
