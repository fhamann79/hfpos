using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.DataProtection;
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

SriRidePdfFontResolver.Register();

builder.Logging.ClearProviders();
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
    {
        Indented = false
    };
});

builder.Services.AddSecurityConfiguration(builder.Configuration, builder.Environment);
builder.Services.AddOperations(builder.Configuration, builder.Environment);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddCheck<PostgresReadinessHealthCheck>("postgres", tags: new[] { "ready" })
    .AddCheck<ApplicationReadiness>("lifecycle", tags: new[] { "ready" });

// DbContext
builder.Services.AddDbContext<PosDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IOptions<StartupSafetyOptions>>().Value.ConnectionStrings.DefaultConnection));

//Auth
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<IPlatformAuthService, PlatformAuthService>();
builder.Services.AddScoped<PasswordRecoveryService>();
builder.Services.AddScoped<IPlatformContextAccessor, PlatformContextAccessor>();
builder.Services.AddScoped<IPlatformTenantService, PlatformTenantService>();
builder.Services.AddScoped<TenantAdministrationGuard>();
builder.Services.AddScoped<IMasterDataLifecycleService, MasterDataLifecycleService>();
builder.Services.AddScoped<IProductCostService, ProductCostService>();
builder.Services.AddScoped<IProductQueryService, ProductQueryService>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<IOperationalContextAccessor, OperationalContextAccessor>();
builder.Services.AddScoped<ICustomerQueryService, CustomerQueryService>();
builder.Services.AddScoped<ISupplierQueryService, SupplierQueryService>();
builder.Services.AddSingleton<IBusinessClockService, BusinessClockService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IInitialDataService, InitialDataService>();
builder.Services.AddScoped<IInventoryTransferService, InventoryTransferService>();
builder.Services.AddScoped<IPosProductLookupService, PosProductLookupService>();
builder.Services.AddScoped<ICashSessionService, CashSessionService>();
builder.Services.AddScoped<IPaymentSettlementService, PaymentSettlementService>();
builder.Services.AddScoped<IPurchaseReceiptQueryService, PurchaseReceiptQueryService>();
builder.Services.AddScoped<IPurchaseReceiptService, PurchaseReceiptService>();
builder.Services.AddScoped<ICreditNoteService, CreditNoteService>();
builder.Services.AddScoped<ICreditNoteInventoryReturnService, CreditNoteInventoryReturnService>();
builder.Services.AddScoped<ICreditNoteRefundService, CreditNoteRefundService>();
builder.Services.AddSingleton<ISriFiscalClock, SriFiscalClock>();
builder.Services.AddScoped<ISriAccessKeyService, SriAccessKeyService>();
builder.Services.AddScoped<ISriXmlDraftService, SriXmlDraftService>();
builder.Services.AddScoped<ISriInvoiceXmlValidator, SriInvoiceXmlValidator>();
builder.Services.AddScoped<ISriCreditNoteXmlDraftService, SriCreditNoteXmlDraftService>();
builder.Services.AddScoped<ISriCreditNoteXmlValidator, SriCreditNoteXmlValidator>();
builder.Services.AddScoped<IFiscalDocumentNumberService, FiscalDocumentNumberService>();
builder.Services.AddScoped<IFiscalSettingsService, FiscalSettingsService>();
builder.Services.AddScoped<ICompanyEmailSettingsService, CompanyEmailSettingsService>();
builder.Services.AddScoped<IEmailSenderService, SmtpEmailSenderService>();
builder.Services.AddScoped<ISriCertificateService, SriCertificateService>();
builder.Services.AddScoped<ISriFiscalReadinessService, SriFiscalReadinessService>();
builder.Services.AddScoped<ISriSigningCertificateProvider, SriSigningCertificateProvider>();
builder.Services.AddSingleton<ISriXadesBesSigner, SriXadesBesSigner>();
builder.Services.AddScoped<ISriInvoiceSigningService, SriInvoiceSigningService>();
builder.Services.AddScoped<ISriCreditNoteSigningService, SriCreditNoteSigningService>();
builder.Services.AddScoped<ISriSubmissionService, SriSubmissionService>();
builder.Services.AddScoped<ElectronicIssuingCoordinator>();
builder.Services.AddScoped<IElectronicIssuingRecoveryService>(services => services.GetRequiredService<ElectronicIssuingCoordinator>());
builder.Services.AddScoped<ElectronicIssuingProcessor>();
builder.Services.AddHostedService<ElectronicIssuingWorker>();
builder.Services.AddScoped<ISriCreditNoteSubmissionService, SriCreditNoteSubmissionService>();
builder.Services.AddScoped<ISriRidePdfService, SriRidePdfService>();
builder.Services.AddScoped<ISaleInvoiceEmailService, SaleInvoiceEmailService>();
builder.Services.AddScoped<ICreditNoteEmailService, CreditNoteEmailService>();
builder.Services.AddScoped<ISalesService, SalesService>();
builder.Services.AddScoped<IElectronicDocumentQueryService, ElectronicDocumentQueryService>();
builder.Services.AddScoped<Pos.Backend.Api.WebApi.Filters.OperationalContextFilter>();
builder.Services.AddHttpClient<ISriWebServiceClient, SriWebServiceClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<SriOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

var operationalDashboardReadPermissions = new[]
{
    AppPermissions.ReportsSalesRead,
    AppPermissions.InventoryRead,
    AppPermissions.CatalogProductsRead,
    AppPermissions.FiscalSettingsRead
};

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AppPolicies.PlatformAdmin, policy => policy.RequireAuthenticatedUser()
        .RequireClaim(PlatformClaims.TokenType, PlatformClaims.TokenTypeValue).RequireRole(PlatformClaims.AdminRole));
    options.AddPolicy(AppPolicies.AdminOnly, policy =>
        policy.RequireRole(AppRoles.Admin));

    options.AddPolicy(AppPolicies.SupervisorOrAdmin, policy =>
        policy.RequireRole(AppRoles.Supervisor, AppRoles.Admin));

    options.AddPolicy(AppPolicies.CashierOrAbove, policy =>
        policy.RequireRole(AppRoles.Cashier, AppRoles.Supervisor, AppRoles.Admin));

    options.AddPolicy(AppPolicies.OperationalDashboardRead, policy =>
        policy.RequireAssertion(context =>
            operationalDashboardReadPermissions.Any(permission =>
                context.User.HasClaim(AppClaims.Permission, permission))));

    options.AddPermissionPolicies(new[]
    {
        AppPermissions.AuthProbeAdmin,
        AppPermissions.AuthProbeSupervisor,
        AppPermissions.AuthProbeCashier,
        AppPermissions.CatalogCategoriesRead,
        AppPermissions.CatalogCategoriesWrite,
        AppPermissions.CatalogProductsRead,
        AppPermissions.CatalogProductsWrite,
        AppPermissions.CustomersRead,
        AppPermissions.CustomersWrite,
        AppPermissions.SuppliersRead,
        AppPermissions.SuppliersWrite,
        AppPermissions.PurchasesRead,
        AppPermissions.PurchasesWrite,
        AppPermissions.CashSessionsRead,
        AppPermissions.CashSessionsWrite,
        AppPermissions.OpStructureRead,
        AppPermissions.OpStructureWrite,
        AppPermissions.PosSalesCreate,
        AppPermissions.InventoryRead,
        AppPermissions.InventoryWrite,
        AppPermissions.PosSalesVoid,
        AppPermissions.ReportsSalesRead,
        AppPermissions.SriDocumentsSign,
        AppPermissions.SriDocumentsSubmit,
        AppPermissions.FiscalSettingsRead,
        AppPermissions.FiscalSettingsWrite,
        AppPermissions.AdminUsersRead,
        AppPermissions.AdminUsersWrite,
        AppPermissions.UsersRecoveryManage,
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
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds),
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt.Key))
        };
    });

var app = builder.Build();
app.Services.ValidateBeforeStartup();
_ = app.Services.GetRequiredService<IOptions<OperationsOptions>>().Value;
_ = app.Services.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
app.Logger.LogInformation("Starting HFPOS release {ReleaseVersion}", OperationsConfiguration.ReleaseVersion);

if (app.Services.GetRequiredService<IOptions<ReverseProxyOptions>>().Value.Enabled)
    app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (app.Environment.IsProduction()) app.UseHsts();
app.UseMiddleware<RequestLoggingScopeMiddleware>();
app.UseWhen(context => !OperationsConfiguration.IsProbe(context.Request.Path), branch => branch.UseHttpsRedirection());
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

// Bootstrap never applies migrations; production must apply the schema through the human runbook.
using (var scope = app.Services.CreateScope())
{
    await PlatformBootstrap.RunAsync(scope.ServiceProvider.GetRequiredService<PosDbContext>(),
        scope.ServiceProvider.GetRequiredService<IOptions<PlatformBootstrapOptions>>().Value);
}

// IMPORTANTE: antes de Authorization
app.UseCors("AllowAngular");
app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseMiddleware<OperationalSessionMiddleware>();
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

public partial class Program { }
