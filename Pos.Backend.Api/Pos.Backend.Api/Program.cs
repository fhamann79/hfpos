using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();

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
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

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
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,

        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"])
        )
    };
});

var app = builder.Build();

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

app.MapControllers();

app.Run();
