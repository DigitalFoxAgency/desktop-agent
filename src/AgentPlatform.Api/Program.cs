using System.Text;
using AgentPlatform.Api.Auth;
using AgentPlatform.Api.Endpoints;
using AgentPlatform.Api.HostedServices;
using AgentPlatform.Api.Hubs;
using AgentPlatform.Api.Middleware;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Auth;
using AgentPlatform.Application.Bridge;
using AgentPlatform.Application.Common;
using AgentPlatform.Application.Modules;
using AgentPlatform.Application.RunContainers;
using AgentPlatform.Application.Runs;
using AgentPlatform.Application.Secrets;
using AgentPlatform.Application.Subscription;
using AgentPlatform.Application.Tenants;
using AgentPlatform.Application.Usage;
using AgentPlatform.Infrastructure.Bridge;
using AgentPlatform.Infrastructure.Inbox;
using AgentPlatform.Infrastructure.Manifests;
using AgentPlatform.Infrastructure.Persistence.Postgres;
using AgentPlatform.Infrastructure.RunContainers;
using AgentPlatform.Infrastructure.Secrets;
using AgentPlatform.Infrastructure.Subscription;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=agentplatform;Username=postgres;Password=postgres";
var modulesRoot = builder.Configuration["AgentPlatform:ModulesRoot"] ?? "modules";
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

builder.Services.AddSingleton(jwtOptions);

builder.Services.AddDbContext<AgentPlatformDbContext>((sp, opt) => opt.UseNpgsql(connectionString));

builder.Services.AddAgentPlatformIdentity(connectionString);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton(new FileSystemModuleSourceOptions { ModulesRoot = modulesRoot });
builder.Services.AddSingleton<IModuleSource, FileSystemModuleSource>();
builder.Services.AddSingleton<IModuleRegistry, ModuleRegistry>();
builder.Services.AddSingleton<JwtIssuer>();

builder.Services.AddScoped<RequestTenantContext>();
builder.Services.AddScoped<IRequestTenantContext>(sp => sp.GetRequiredService<RequestTenantContext>());

builder.Services.AddScoped<ITenantRepository, PostgresTenantRepository>();
builder.Services.AddScoped<IWorkflowRunRepository, PostgresWorkflowRunRepository>();
builder.Services.AddScoped<IAuditLog, PostgresAuditLog>();
builder.Services.AddScoped<IUsageMeter, PostgresUsageMeter>();
builder.Services.AddScoped<IInboxNotifier, NoOpInboxNotifier>();
builder.Services.AddScoped<ISubscriptionGate, DefaultSubscriptionGate>();

builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<PhaseAssignmentService>();
builder.Services.AddScoped<WorkflowRunService>();
builder.Services.AddScoped<IAuthBackend, IdentityAuthBackend>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<IPhaseRunRepository, PostgresPhaseRunRepository>();

builder.Services.Configure<PhaseSessionOptions>(builder.Configuration.GetSection("AgentPlatform:PhaseSession"));

builder.Services.AddSingleton(sp =>
    new RunVolumeOptions
    {
        RunsRoot = builder.Configuration["AgentPlatform:Runs:Root"] ?? "/var/lib/agency/runs",
        ArchiveRoot = builder.Configuration["AgentPlatform:Runs:ArchiveRoot"] ?? "/var/lib/agency/archive",
    });
builder.Services.AddSingleton<RunVolumeManager>();
builder.Services.AddSingleton(sp => new DockerDriverOptions
{
    DockerEndpoint = builder.Configuration["AgentPlatform:Docker:Endpoint"]
        ?? (OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock"),
    NetworkMode = builder.Configuration["AgentPlatform:Docker:NetworkMode"] ?? "bridge",
    RunAsUser = builder.Configuration["AgentPlatform:Docker:RunAsUser"],
    ModulesHostPath = builder.Configuration["AgentPlatform:Docker:ModulesHostPath"]
        ?? Path.GetFullPath(modulesRoot),
});
builder.Services.AddSingleton<IRunContainerDriver, DockerRunContainerDriver>();
builder.Services.AddSingleton<BridgeConnectionRegistry>();
builder.Services.AddSingleton<IBridgeChannelFactory>(sp => sp.GetRequiredService<BridgeConnectionRegistry>());
builder.Services.AddSingleton<ISecretStore>(sp =>
{
    var keyEnv = builder.Configuration["AgentPlatform:Vault:KeyBase64"]
        ?? Environment.GetEnvironmentVariable("AGP_VAULT_KEY_BASE64")
        ?? Convert.ToBase64String(new byte[32]);
    var dir = builder.Configuration["AgentPlatform:Vault:Directory"] ?? "/var/lib/agency/vault";
    return new FileEncryptedSecretStore(new FileEncryptedSecretStoreOptions
    {
        VaultDirectory = dir,
        MasterKeyBase64 = keyEnv,
    });
});
builder.Services.AddSingleton<IPhaseSessionService, PhaseSessionService>();

builder.Services.AddHostedService<DatabaseMigrator>();
builder.Services.AddHostedService<ModuleRegistrySeeder>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

app.UseCors();
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();
app.UseTenantScope();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapAuthEndpoints();
app.MapTenantEndpoints();
app.MapModuleEndpoints();
app.MapRunEndpoints();
app.MapInboxEndpoints();
app.MapPhaseEndpoints();
app.MapBridgeHub();
app.MapPhaseSessionHub();

app.Run();

namespace AgentPlatform.Api
{
    /// <summary>Public marker so test hosts can reference the assembly.</summary>
    public partial class Program;
}
