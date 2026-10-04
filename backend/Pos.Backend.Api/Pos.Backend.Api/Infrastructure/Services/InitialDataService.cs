using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class InitialDataService(PosDbContext db, IOperationalContextAccessor accessor,
    TenantAdministrationGuard guard, IInventoryService inventory, IProductCostService costs,
    IDataProtectionProvider protection) : IInitialDataService
{
    private const int MaxRows = 500;
    private const int MaxBytes = 1048576;
    private readonly IDataProtector protector = protection.CreateProtector("HFPOS.InitialDataPreview.v1");
    private static readonly Dictionary<string, string[]> Headers = new()
    {
        ["categories"] = ["name"],
        ["products"] = ["internalCode", "barcode", "name", "category", "price", "cost", "minimumStock", "vatCategory"],
        ["customers"] = ["name", "identificationType", "identification", "phone", "email", "address", "notes"],
        ["suppliers"] = ["name", "identification", "phone", "email", "address", "notes"],
        ["opening-inventory"] = ["internalCode", "quantity"]
    };

    public async Task<string> GetTemplateAsync(string kind)
    {
        var ctx = await accessor.GetRequiredContextAsync();
        await RequirePermissionsAsync(ctx, kind);
        return string.Join(',', Headers[kind]) + "\r\n";
    }

    public async Task<InitialDataPreviewDto> PreviewAsync(InitialDataPreviewRequest request)
    {
        var ctx = await accessor.GetRequiredContextAsync();
        await RequirePermissionsAsync(ctx, request.Kind);
        var preview = Parse(request);
        // No persistent preview record; authenticate the reviewed payload and resolved references.
        await using var tx = await db.Database.BeginTransactionAsync();
        await guard.LockOperationalWriteAsync(ctx);
        await RequirePermissionsAsync(ctx, request.Kind);
        await ValidateCurrentStateAsync(preview, ctx);
        preview.CanConfirm = preview.Errors.Count == 0 && preview.Rows.Count > 0
            && preview.Rows.All(r => r.Errors.Count == 0);
        if (preview.CanConfirm)
            preview.PreviewToken = protector.Protect(JsonSerializer.Serialize(new PreviewProof(
                request.RequestId, Hash(request), ctx.CompanyId, ctx.EstablishmentId, ctx.EmissionPointId,
                ctx.UserId, preview.Rows.Select(r => r.ResolvedId).ToArray(), DateTime.UtcNow.AddHours(2))));
        await tx.CommitAsync();
        return preview;
    }

    public async Task<InitialDataResultDto> ConfirmAsync(InitialDataConfirmRequest request)
    {
        var ctx = await accessor.GetRequiredContextAsync();
        var payload = request.Payload;
        var preview = Parse(payload);
        var hash = Hash(payload);
        await using var tx = await guard.BeginChangeAsync(ctx.CompanyId);
        await guard.LockOperationalWriteAsync(ctx);
        await RequirePermissionsAsync(ctx, payload.Kind);
        var existing = await db.InitialDataBatches.AsNoTracking()
            .SingleOrDefaultAsync(b => b.CompanyId == ctx.CompanyId && b.RequestId == payload.RequestId);
        if (existing is not null)
        {
            if (existing.PayloadHash != hash || existing.Kind != payload.Kind
                || existing.EstablishmentId != ctx.EstablishmentId || existing.EmissionPointId != ctx.EmissionPointId
                || existing.UserId != ctx.UserId)
                throw new InvalidOperationException("INITIAL_DATA_REQUEST_CONFLICT");
            return JsonSerializer.Deserialize<InitialDataResultDto>(existing.ResultJson)!;
        }
        PreviewProof? proof;
        try { proof = JsonSerializer.Deserialize<PreviewProof>(protector.Unprotect(request.PreviewToken)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException)
        { throw new InvalidOperationException("INITIAL_DATA_PREVIEW_INVALID", ex); }
        if (proof is null || proof.RequestId != payload.RequestId || proof.Hash != hash
            || proof.CompanyId != ctx.CompanyId || proof.EstablishmentId != ctx.EstablishmentId
            || proof.EmissionPointId != ctx.EmissionPointId || proof.UserId != ctx.UserId
            || proof.ExpiresAt < DateTime.UtcNow)
            throw new InvalidOperationException("INITIAL_DATA_PREVIEW_INVALID");
        await ValidateCurrentStateAsync(preview, ctx);
        if (preview.Errors.Count != 0 || preview.Rows.Any(r => r.Errors.Count != 0)
            || !preview.Rows.Select(r => r.ResolvedId).SequenceEqual(proof.ResolvedIds))
            throw new InvalidOperationException("INITIAL_DATA_REVALIDATION_FAILED");
        var batch = new InitialDataBatch
        {
            CompanyId = ctx.CompanyId, EstablishmentId = ctx.EstablishmentId,
            EmissionPointId = ctx.EmissionPointId, UserId = ctx.UserId, RequestId = payload.RequestId,
            Kind = payload.Kind, PayloadHash = hash, RowCount = preview.Rows.Count, CreatedAt = DateTime.UtcNow
        };
        db.InitialDataBatches.Add(batch);
        await db.SaveChangesAsync();
        var ids = await CreateAsync(preview, ctx, batch.Id);
        var result = new InitialDataResultDto
        {
            BatchId = batch.Id, RequestId = batch.RequestId, Kind = batch.Kind,
            CompanyId = batch.CompanyId, EstablishmentId = batch.EstablishmentId,
            EmissionPointId = batch.EmissionPointId, UserId = batch.UserId, RowCount = batch.RowCount,
            CreatedIds = ids, CreatedAt = batch.CreatedAt,
            RowNumbers = preview.Rows.Select(r => r.RowNumber).ToList()
        };
        batch.ResultJson = JsonSerializer.Serialize(result);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return result;
    }

    public async Task<IReadOnlyList<InitialDataResultDto>> GetBatchesAsync(int page = 1)
    {
        var ctx = await accessor.GetRequiredContextAsync();
        await RequirePermissionAsync(ctx, AppPermissions.OpStructureRead);
        var results = await db.InitialDataBatches.AsNoTracking().Where(b => b.CompanyId == ctx.CompanyId)
            .OrderByDescending(b => b.Id).Skip((int)Math.Min((long)(Math.Max(page, 1) - 1) * 30, int.MaxValue))
            .Take(30).Select(b => b.ResultJson).ToListAsync();
        return results.Select(s => JsonSerializer.Deserialize<InitialDataResultDto>(s)!).ToList();
    }

    public async Task<TenantReadinessDto> GetReadinessAsync()
    {
        var ctx = await accessor.GetRequiredContextAsync();
        foreach (var permission in new[] { AppPermissions.OpStructureRead, AppPermissions.FiscalSettingsRead, AppPermissions.AdminUsersRead })
            await RequirePermissionAsync(ctx, permission);
        var company = await db.Companies.AsNoTracking().Where(c => c.Id == ctx.CompanyId)
            .Select(c => new { c.Ruc, c.MatrixAddress }).SingleAsync();
        // Safe metadata projection only: never call certificate diagnostics or get-or-create settings.
        var sri = await db.CompanySriSettings.AsNoTracking().Where(s => s.CompanyId == ctx.CompanyId)
            .Select(s => new { s.IsEnabled, s.Environment, s.EmissionType }).SingleOrDefaultAsync();
        var certificate = await db.CompanySriCertificates.AnyAsync(c => c.CompanyId == ctx.CompanyId
            && c.IsActive && c.NotBefore <= DateTime.UtcNow && c.NotAfter > DateTime.UtcNow && c.HasPrivateKey);
        var structure = await db.Establishments.AsNoTracking().AnyAsync(e => e.Id == ctx.EstablishmentId
            && e.CompanyId == ctx.CompanyId && e.IsActive && e.Code != "000" && e.Code.Length == 3
            && e.Address.Trim() != "" && e.Address != "N/A"
            && e.EmissionPoints.Any(p => p.Id == ctx.EmissionPointId && p.IsActive && p.Code != "000" && p.Code.Length == 3));
        var checks = new List<TenantReadinessCheckDto>
        {
            new("company", Regex.IsMatch(company.Ruc, "^[0-9]{13}$")
                && !string.IsNullOrWhiteSpace(company.MatrixAddress) && company.MatrixAddress != "N/A", "fiscal-settings"),
            new("structure", structure, "operational-structure"),
            new("users", await guard.HasActiveAdministratorAsync(ctx.CompanyId), "administration"),
            new("categories", await db.Categories.AnyAsync(c => c.CompanyId == ctx.CompanyId && c.IsActive), "catalog"),
            new("products", await db.Products.AnyAsync(p => p.CompanyId == ctx.CompanyId && p.IsActive), "catalog"),
            new("customers", await db.Customers.AnyAsync(c => c.CompanyId == ctx.CompanyId && c.IsActive), "customers"),
            new("suppliers", await db.Suppliers.AnyAsync(s => s.CompanyId == ctx.CompanyId && s.IsActive), "suppliers"),
            new("opening-inventory", await db.InitialDataBatches.AnyAsync(b => b.CompanyId == ctx.CompanyId
                && b.EstablishmentId == ctx.EstablishmentId && b.Kind == "opening-inventory"), "inventory"),
            new("fiscal-configuration", sri is { IsEnabled: true, Environment: 1 or 2, EmissionType: 1 } && certificate, "fiscal-settings")
        };
        return new(ctx.CompanyId, ctx.EstablishmentId, ctx.EmissionPointId, checks.All(c => c.Ready), checks);
    }

    private static InitialDataPreviewDto Parse(InitialDataPreviewRequest request)
    {
        if (!Headers.TryGetValue(request.Kind, out var headers)) throw new InvalidOperationException("INITIAL_DATA_KIND_INVALID");
        var result = new InitialDataPreviewDto { RequestId = request.RequestId, Kind = request.Kind };
        if (request.RequestId == Guid.Empty) result.Errors.Add("REQUEST_ID_REQUIRED");
        if (request.DuplicatePolicy != "create-only") result.Errors.Add("DUPLICATE_POLICY_INVALID");
        if (string.IsNullOrWhiteSpace(request.Csv) || Encoding.UTF8.GetByteCount(request.Csv) > MaxBytes)
        { result.Errors.Add("CSV_SIZE_INVALID"); return result; }
        using var parser = new TextFieldParser(new StringReader(request.Csv.TrimStart('\uFEFF')))
        { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(",");
        try
        {
            var actual = parser.ReadFields();
            if (actual is null || !actual.SequenceEqual(headers))
            { result.Errors.Add("CSV_HEADER_INVALID"); return result; }
            while (!parser.EndOfData)
            {
                if (result.Rows.Count == MaxRows) { result.Errors.Add("CSV_ROW_LIMIT"); break; }
                var line = (int)parser.LineNumber;
                var fields = parser.ReadFields()!;
                var row = new InitialDataRowDto { RowNumber = line };
                if (fields.Length != headers.Length) row.Errors.Add("CSV_COLUMN_COUNT_INVALID");
                for (var i = 0; i < headers.Length; i++) row.Values[headers[i]] = i < fields.Length ? fields[i].Trim() : "";
                ValidateRow(request.Kind, row);
                result.Rows.Add(row);
            }
        }
        catch (MalformedLineException)
        { result.Rows.Add(new InitialDataRowDto { RowNumber = (int)parser.ErrorLineNumber, Errors = ["CSV_MALFORMED"] }); }
        if (result.Rows.Count == 0) result.Errors.Add("CSV_EMPTY");
        return result;
    }

    private static void ValidateRow(string kind, InitialDataRowDto row)
    {
        var v = row.Values;
        void Text(string key, int max, bool required = false)
        {
            if (v[key].Length > max || (required && v[key].Length == 0)) row.Errors.Add($"{key}:INVALID");
        }
        if (kind != "opening-inventory") Text("name", 150, true);
        if (kind is "products" or "opening-inventory") Text("internalCode", 100, true);
        if (kind == "products")
        {
            Text("barcode", 100); Text("category", 150, true);
            foreach (var key in new[] { "price", "cost", "minimumStock" })
                if (!ValidDecimal(v[key], key == "price" ? 2 : 4)) row.Errors.Add($"{key}:INVALID_DECIMAL");
            if (!Enum.TryParse<ProductVatCategory>(v["vatCategory"], out var vat)
                || !Enum.IsDefined(vat) || vat.ToString() != v["vatCategory"])
                row.Errors.Add("vatCategory:INVALID");
        }
        if (kind == "opening-inventory" && !ValidDecimal(v["quantity"], 4)) row.Errors.Add("quantity:INVALID_DECIMAL");
        if (kind is "customers" or "suppliers")
        {
            // Imports require a stable natural key; optional-identity manual creation stays unchanged.
            Text("identification", 20, true); Text("phone", 30);
            Text("email", 320); Text("address", kind == "customers" ? 300 : 250); Text("notes", 500);
            if (v["email"] != "" && !Regex.IsMatch(v["email"], @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) row.Errors.Add("email:INVALID");
        }
        if (kind == "customers")
        {
            var id = v["identification"];
            var valid = v["identificationType"] switch
            {
                "04" => Regex.IsMatch(id, "^[0-9]{13}$"), "05" => Regex.IsMatch(id, "^[0-9]{10}$"),
                "06" => Regex.IsMatch(id, "^[A-Za-z0-9]+$"), "07" => id == "9999999999999", _ => false
            };
            if (!valid) row.Errors.Add("identification:INVALID");
        }
    }

    private async Task ValidateCurrentStateAsync(InitialDataPreviewDto preview, OperationalContext ctx)
    {
        var rows = preview.Rows.Where(r => r.Values.Count > 0).ToList();
        var names = rows.Where(r => r.Values.ContainsKey("name")).Select(r => r.Values["name"].ToLowerInvariant()).Distinct().ToArray();
        var codes = rows.Where(r => r.Values.ContainsKey("internalCode")).Select(r => r.Values["internalCode"]).Distinct().ToArray();
        var ids = rows.Where(r => r.Values.ContainsKey("identification")).Select(r => r.Values["identification"]).Where(s => s != "").Distinct().ToArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (preview.Kind == "categories")
        {
            var existing = await db.Categories.AsNoTracking().Where(c => c.CompanyId == ctx.CompanyId && names.Contains(c.Name.ToLower()))
                .Select(c => c.Name.ToLower()).ToListAsync();
            foreach (var r in rows) Duplicate(r, r.Values["name"].ToLowerInvariant(), seen, existing);
        }
        else if (preview.Kind == "products")
        {
            var barcodes = rows.Select(r => r.Values["barcode"]).Where(s => s != "").Distinct().ToArray();
            var existing = await db.Products.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId
                && (codes.Contains(p.InternalCode!) || barcodes.Contains(p.Barcode!)))
                .Select(p => new { p.InternalCode, p.Barcode }).ToListAsync();
            var categories = rows.Select(r => r.Values["category"]).Distinct().ToArray();
            var active = await db.Categories.AsNoTracking().Where(c => c.CompanyId == ctx.CompanyId && c.IsActive && categories.Contains(c.Name))
                .Select(c => new { c.Id, c.Name }).ToListAsync();
            foreach (var r in rows)
            {
                Duplicate(r, "code:" + r.Values["internalCode"], seen, existing.Select(p => "code:" + p.InternalCode));
                if (r.Values["barcode"] != "") Duplicate(r, "barcode:" + r.Values["barcode"], seen, existing.Select(p => "barcode:" + p.Barcode));
                r.ResolvedId = active.SingleOrDefault(c => c.Name == r.Values["category"])?.Id;
                if (r.ResolvedId is null) r.Errors.Add("category:NOT_ACTIVE_IN_COMPANY");
            }
        }
        else if (preview.Kind is "customers" or "suppliers")
        {
            var existing = preview.Kind == "customers"
                ? await db.Customers.AsNoTracking().Where(c => c.CompanyId == ctx.CompanyId && ids.Contains(c.Identification!))
                    .Select(c => c.Identification!).ToListAsync()
                : await db.Suppliers.AsNoTracking().Where(s => s.CompanyId == ctx.CompanyId && ids.Contains(s.Identification!))
                    .Select(s => s.Identification!).ToListAsync();
            foreach (var r in rows) Duplicate(r, r.Values["identification"], seen, existing);
        }
        else if (preview.Kind == "opening-inventory")
        {
            var products = await db.Products.AsNoTracking().Where(p => p.CompanyId == ctx.CompanyId && codes.Contains(p.InternalCode!))
                .Select(p => new { p.Id, p.InternalCode, p.IsActive,
                    Quantity = db.ProductStocks.Where(s => s.CompanyId == ctx.CompanyId && s.EstablishmentId == ctx.EstablishmentId && s.ProductId == p.Id)
                        .Select(s => (decimal?)s.Quantity).FirstOrDefault() ?? 0m,
                    Watermark = db.InventoryMovements.Where(m => m.CompanyId == ctx.CompanyId && m.EstablishmentId == ctx.EstablishmentId && m.ProductId == p.Id)
                        .Max(m => (int?)m.Id) ?? 0 }).ToListAsync();
            foreach (var r in rows)
            {
                Duplicate(r, r.Values["internalCode"], seen, []);
                var product = products.SingleOrDefault(p => p.InternalCode == r.Values["internalCode"]);
                r.ResolvedId = product?.Id;
                if (product is null || !product.IsActive) r.Errors.Add("internalCode:NOT_ACTIVE_IN_COMPANY");
                else if (product.Quantity != 0m || product.Watermark != 0) r.Errors.Add("OPENING_INVENTORY_ALREADY_USED");
            }
        }
    }

    private async Task<List<int>> CreateAsync(InitialDataPreviewDto preview, OperationalContext ctx, int batchId)
    {
        var entities = new List<object>();
        var movementIds = new List<int>();
        foreach (var row in preview.Rows)
        {
            var v = row.Values;
            string? Optional(string key) => v[key] == "" ? null : v[key];
            object? entity = preview.Kind switch
            {
                "categories" => new Category { CompanyId = ctx.CompanyId, Name = v["name"], IsActive = true, CreatedAt = DateTime.UtcNow },
                "products" => new Product { CompanyId = ctx.CompanyId, CategoryId = row.ResolvedId!.Value,
                    Name = v["name"], InternalCode = v["internalCode"], Barcode = Optional("barcode"),
                    Price = Number(v["price"]), Cost = Number(v["cost"]), MinimumStock = Number(v["minimumStock"]),
                    VatCategory = Enum.Parse<ProductVatCategory>(v["vatCategory"]), IsActive = true, CreatedAt = DateTime.UtcNow },
                "customers" => new Customer { CompanyId = ctx.CompanyId, Name = v["name"], Identification = v["identification"],
                    IdentificationType = v["identificationType"], Phone = Optional("phone"), Email = Optional("email"),
                    Address = Optional("address"), Notes = Optional("notes"), IsActive = true, CreatedAt = DateTime.UtcNow },
                "suppliers" => new Supplier { CompanyId = ctx.CompanyId, Name = v["name"], Identification = v["identification"],
                    Phone = Optional("phone"), Email = Optional("email"), Address = Optional("address"), Notes = Optional("notes"),
                    IsActive = true, CreatedAt = DateTime.UtcNow },
                _ => null
            };
            if (entity is not null) { db.Add(entity); entities.Add(entity); }
            else movementIds.Add((await inventory.RegisterOpeningAsync(row.ResolvedId!.Value, Number(v["quantity"]), batchId, row.RowNumber)).Id);
        }
        await db.SaveChangesAsync();
        foreach (var product in entities.OfType<Product>()) costs.InitializeManualCost(product, ctx.UserId, product.CreatedAt);
        await db.SaveChangesAsync();
        return entities.Select(e => (int)db.Entry(e).Property("Id").CurrentValue!).Concat(movementIds).ToList();
    }

    private async Task RequirePermissionsAsync(OperationalContext ctx, string kind)
    {
        var permissions = kind switch
        {
            "categories" => new[] { AppPermissions.CatalogCategoriesRead, AppPermissions.CatalogCategoriesWrite },
            "products" => [AppPermissions.CatalogProductsRead, AppPermissions.CatalogProductsWrite, AppPermissions.CatalogCategoriesRead],
            "customers" => [AppPermissions.CustomersRead, AppPermissions.CustomersWrite],
            "suppliers" => [AppPermissions.SuppliersRead, AppPermissions.SuppliersWrite],
            "opening-inventory" => [AppPermissions.InventoryRead, AppPermissions.InventoryWrite, AppPermissions.CatalogProductsRead],
            _ => throw new InvalidOperationException("INITIAL_DATA_KIND_INVALID")
        };
        foreach (var permission in permissions) await RequirePermissionAsync(ctx, permission);
    }
    private async Task RequirePermissionAsync(OperationalContext ctx, string permission)
    {
        if (!await db.Users.AnyAsync(u => u.Id == ctx.UserId && u.CompanyId == ctx.CompanyId && u.IsActive
            && u.Role.IsActive && u.Role.CompanyId == ctx.CompanyId
            && u.Role.RolePermissions.Any(rp => rp.Permission.IsActive && rp.Permission.Code == permission)))
            throw new OperationalContextException("FORBIDDEN", 403);
    }
    private static void Duplicate(InitialDataRowDto row, string key, HashSet<string> seen, IEnumerable<string> existing)
    { if (!seen.Add(key) || existing.Contains(key)) row.Errors.Add("DUPLICATE_CREATE_ONLY"); }
    private static bool ValidDecimal(string value, int scale) => Regex.IsMatch(value, $"^[0-9]+(\\.[0-9]{{1,{scale}}})?$")
        && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
        && number < (scale == 2 ? 10000000000000000m : 100000000000000m);
    private static decimal Number(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    private static string Hash(InitialDataPreviewRequest request)
    {
        var parsed = Parse(request);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            Schema = 1, request.Kind, request.DuplicatePolicy, parsed.Errors,
            Rows = parsed.Rows.Select(r => new { r.Values, r.Errors })
        }))));
    }
    private sealed record PreviewProof(Guid RequestId, string Hash, int CompanyId, int EstablishmentId,
        int EmissionPointId, int UserId, int?[] ResolvedIds, DateTime ExpiresAt);
}
