using MyApp.API.Extensions;
using MyApp.Application;
using MyApp.Application.Security;
using MyApp.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is required.");
var jwtSection = builder.Configuration.GetRequiredSection("Jwt");
var jwtOptions = new JwtOptions(
    jwtSection.GetValue<string>("Key")
        ?? throw new InvalidOperationException("Configuration value 'Jwt:Key' is required."),
    jwtSection.GetValue<string>("Issuer")
        ?? throw new InvalidOperationException("Configuration value 'Jwt:Issuer' is required."),
    jwtSection.GetValue<string>("Audience")
        ?? throw new InvalidOperationException("Configuration value 'Jwt:Audience' is required."));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(
    connectionString,
    jwtOptions);
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorization();
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
