using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Console;
using Pos.Backend.Api.HealthChecks;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.OpenApi.Models;
using Pos.Backend.Api.WebApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
    {
        Indented = false
    };
});

var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(defaultConnection))
{
    throw new InvalidOperationException(
        "Missing required configuration 'ConnectionStrings:DefaultConnection'. Configure it via .NET User Secrets, appsettings, or environment variables.");
}

builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection("Jwt"));

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwtOptions.Key))
{
    throw new InvalidOperationException(
        "Missing required configuration 'Jwt:Key'. Configure it via .NET User Secrets, appsettings, or environment variables.");
}

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddCheck<PostgresReadinessHealthCheck>("postgres", tags: new[] { "ready" });

// CORS para Angular
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular",
        policy =>
        {
            policy
                .WithOrigins("http://localhost:4200")
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
});

// DbContext
builder.Services.AddDbContext<PosDbContext>(options =>
    options.UseNpgsql(defaultConnection));

//Auth
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<IOperationalContextAccessor, OperationalContextAccessor>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<ISalesService, SalesService>();
builder.Services.AddScoped<Pos.Backend.Api.WebApi.Filters.OperationalContextFilter>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AppPolicies.AdminOnly, policy =>
        policy.RequireRole(AppRoles.Admin));

    options.AddPolicy(AppPolicies.SupervisorOrAdmin, policy =>
        policy.RequireRole(AppRoles.Supervisor, AppRoles.Admin));

    options.AddPolicy(AppPolicies.CashierOrAbove, policy =>
        policy.RequireRole(AppRoles.Cashier, AppRoles.Supervisor, AppRoles.Admin));

    options.AddPermissionPolicies(new[]
    {
        AppPermissions.AuthProbeAdmin,
        AppPermissions.AuthProbeSupervisor,
        AppPermissions.AuthProbeCashier,
        AppPermissions.CatalogCategoriesRead,
        AppPermissions.CatalogCategoriesWrite,
        AppPermissions.CatalogProductsRead,
        AppPermissions.CatalogProductsWrite,
        AppPermissions.OpStructureRead,
        AppPermissions.OpStructureWrite,
        AppPermissions.PosSalesCreate,
        AppPermissions.InventoryRead,
        AppPermissions.InventoryWrite,
        AppPermissions.PosSalesVoid,
        AppPermissions.ReportsSalesRead,
        AppPermissions.AdminUsersRead,
        AppPermissions.AdminUsersWrite,
        AppPermissions.AdminRolesRead,
        AppPermissions.AdminRolesWrite
    });
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "POS Backend API",
        Version = "v1"
    });

    // 1) Definimos el tipo de seguridad (JWT Bearer)
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Ingresa: Bearer {tu_token}"
    });

    // 2) Le decimos a Swagger que aplique esa seguridad a los endpoints
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// JWT Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptionsAccessor) =>
    {
        var jwt = jwtOptionsAccessor.Value;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt.Key))
        };
    });

var app = builder.Build();

app.UseMiddleware<RequestLoggingScopeMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Swagger solo en desarrollo
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    var seedDemoData = app.Configuration.GetValue<bool>("SeedDemoData");

    if (seedDemoData)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PosDbContext>();

        // 🔑 CLAVE: asegurar que las migraciones estén aplicadas
        await context.Database.MigrateAsync();

        await SeedData.SeedDevelopmentAsync(context);
    }
}

app.UseHttpsRedirection();

// IMPORTANTE: antes de Authorization
app.UseCors("AllowAngular");

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = HealthCheckResponseWriter.WriteJsonAsync
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthCheckResponseWriter.WriteJsonAsync
});

app.MapControllers();

app.Run();
