using IdentityProvider.Infrastructure.Extensions;
using IdentityProvider.Infrastructure.Identity;
using IdentityProvider.Infrastructure.Identity.OpenIdConnect;
using IdentityProvider.Infrastructure.Identity.Persistence;
using IdentityProvider.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using Microsoft.OpenApi;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

if (args.Contains("--prepare-development", StringComparer.Ordinal))
{
    if (!builder.Environment.IsDevelopment()) throw new InvalidOperationException("Development setup is only available in Development.");
    DevelopmentSetup.Prepare(builder.Environment.ContentRootPath);
    return;
}
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddJsonFile("appsettings.Development.Local.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables().AddCommandLine(args);

builder.Host.UseSerilog((_, configuration) => configuration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("OpenIddict", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddCors();
var dataProtectionDirectory = builder.Configuration["DataProtection:KeyPath"]
    ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "data", "keys"));
Directory.CreateDirectory(dataProtectionDirectory);
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("IdentityProvider")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionDirectory));
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = builder.Configuration["ServiceName"] ?? "Project Template API",
        Version = "v1"
    });
});
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
var knownProxies = builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
if (knownProxies.Length != 0)
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var address in knownProxies) options.KnownProxies.Add(IPAddress.Parse(address));
    });
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddIdentityProviderServer(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (args.Contains("--initialize-database", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    var provider = builder.Configuration["Identity:DatabaseProvider"] ?? "SqlServer";
    if (provider == "Sqlite")
    {
        if (!app.Environment.IsDevelopment()) throw new InvalidOperationException("SQLite EnsureCreated is development-only; use SQL Server migrations in production.");
        await database.Database.EnsureCreatedAsync();
    }
    else await database.Database.MigrateAsync();
    Console.WriteLine("Identity database initialized.");
    return;
}
if (args.Contains("--register-clients", StringComparer.Ordinal))
{
    await app.Services.RegisterIdentityProviderClientsAsync();
    Console.WriteLine("Configured clients and scopes registered.");
    return;
}

if (args.Contains("--initialize-identity", StringComparer.Ordinal))
{
    await app.Services.InitializeIdentityAsync();
    return;
}

app.UseExceptionHandler();
if (knownProxies.Length != 0) app.UseForwardedHeaders();
app.UseSerilogRequestLogging();
app.UseRouting();
app.UseCors(OpenIdConnectModule.CorsPolicy);
app.UseAuthentication();
app.UseSwaggerUI();
app.MapSwagger().AllowAnonymous();
app.UseAuthorization();
app.UseRateLimiter();
app.MapHealthChecks("/health/live").AllowAnonymous();
if (builder.Configuration["Identity:Provider"] == "Local")
    app.MapGet("/health/ready", async (IdentityDbContext database, CancellationToken cancellationToken) =>
    {
        try
        {
            await database.Users.AsNoTracking().AnyAsync(cancellationToken);
            return Results.Ok(new { status = "ready" });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }).AllowAnonymous();
app.MapControllers();
app.MapIdentityModule();
app.MapRazorPages();
app.MapGet("/", () => Results.Redirect("/account/login")).AllowAnonymous();

app.Run();

public partial class Program { }
