using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class SalesService : ISalesService
{
    private readonly PosDbContext _context;
    private readonly ILogger<SalesService> _logger;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly IInventoryService _inventoryService;
    private readonly ISriAccessKeyService _sriAccessKeyService;
    private readonly ISriXmlDraftService _sriXmlDraftService;
    private readonly ISriInvoiceXmlValidator _sriInvoiceXmlValidator;
    private readonly SriOptions _sriOptions;

    public SalesService(
        PosDbContext context,
        ILogger<SalesService> logger,
        IOperationalContextAccessor operationalContextAccessor,
        IInventoryService inventoryService,
        ISriAccessKeyService sriAccessKeyService,
        ISriXmlDraftService sriXmlDraftService,
        ISriInvoiceXmlValidator sriInvoiceXmlValidator,
        IOptions<SriOptions> sriOptions)
    {
        _context = context;
        _logger = logger;
        _operationalContextAccessor = operationalContextAccessor;
        _inventoryService = inventoryService;
        _sriAccessKeyService = sriAccessKeyService;
        _sriXmlDraftService = sriXmlDraftService;
        _sriInvoiceXmlValidator = sriInvoiceXmlValidator;
        _sriOptions = sriOptions.Value;
    }

    public async Task<IReadOnlyList<SaleListItemDto>> GetSalesAsync(DateTime? from, DateTime? to, SaleStatus? status, string? search, int? userId)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var query = _context.Sales
            .AsNoTracking()
            .Where(s => s.CompanyId == operationalContext.CompanyId
                && s.EstablishmentId == operationalContext.EstablishmentId
                && s.EmissionPointId == operationalContext.EmissionPointId);

        if (from.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(from.Value, DateTimeKind.Utc);
            query = query.Where(s => s.CreatedAt >= fromUtc);
        }

        if (to.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(to.Value, DateTimeKind.Utc);
            query = query.Where(s => s.CreatedAt <= toUtc);
        }

        if (status.HasValue)
        {
            query = query.Where(s => s.Status == status.Value);
        }

        if (userId.HasValue)
        {
            query = query.Where(s => s.UserId == userId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(s =>
                (s.Notes != null && s.Notes.ToLower().Contains(term))
                || (s.Number != null && s.Number.ToLower().Contains(term))
                || (s.EstablishmentCodeSnapshot != null && s.EstablishmentCodeSnapshot.Contains(term))
                || (s.EmissionPointCodeSnapshot != null && s.EmissionPointCodeSnapshot.Contains(term))
                || s.Id.ToString().Contains(term));
        }

        return await query
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Select(s => new SaleListItemDto
            {
                Id = s.Id,
                Status = s.Status,
                Number = s.Number,
                DocumentType = s.DocumentType,
                DocumentStatus = s.DocumentStatus,
                Total = s.Total,
                ItemsCount = s.Items.Count,
                CreatedAt = s.CreatedAt,
                UserId = s.UserId,
                Username = s.User.Username,
                Notes = s.Notes
            })
            .ToListAsync();
    }

    public async Task<SaleDto?> GetByIdAsync(int id)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        return await _context.Sales
            .AsNoTracking()
            .Where(s => s.Id == id
                && s.CompanyId == operationalContext.CompanyId
                && s.EstablishmentId == operationalContext.EstablishmentId
                && s.EmissionPointId == operationalContext.EmissionPointId)
            .Select(s => new SaleDto
            {
                Id = s.Id,
                Status = s.Status,
                CustomerId = s.CustomerId,
                CustomerName = s.Customer != null ? s.Customer.Name : null,
                PaymentMethod = s.PaymentMethod,
                DocumentType = s.DocumentType,
                DocumentStatus = s.DocumentStatus,
                Number = s.Number,
                EstablishmentCodeSnapshot = s.EstablishmentCodeSnapshot,
                EmissionPointCodeSnapshot = s.EmissionPointCodeSnapshot,
                Sequential = s.Sequential,
                DocumentIssuedAt = s.DocumentIssuedAt,
                AccessKey = s.AccessKey,
                AuthorizationNumber = s.AuthorizationNumber,
                AuthorizedAt = s.AuthorizedAt,
                SriEnvironment = s.SriEnvironment,
                SriEmissionType = s.SriEmissionType,
                SriNumericCode = s.SriNumericCode,
                SriXmlGeneratedAt = s.SriXmlGeneratedAt,
                HasSriXmlDraft = s.SriXmlDraft != null,
                SriSignedAt = s.SriSignedAt,
                HasSriSignedXml = s.SriSignedXml != null,
                SriSignatureHash = s.SriSignatureHash,
                SriSigningCertificateThumbprint = s.SriSigningCertificateThumbprint,
                SriSigningCertificateSubject = s.SriSigningCertificateSubject,
                SriSigningCertificateSerialNumber = s.SriSigningCertificateSerialNumber,
                SriSubmittedAt = s.SriSubmittedAt,
                SriReceptionStatus = s.SriReceptionStatus,
                SriAuthorizationStatus = s.SriAuthorizationStatus,
                SriLastSubmissionError = s.SriLastSubmissionError,
                SriLastCheckedAt = s.SriLastCheckedAt,
                GrossSubtotal = s.GrossSubtotal,
                DiscountAmount = s.DiscountAmount,
                Subtotal = s.Subtotal,
                TaxAmount = s.TaxAmount,
                Vat15Subtotal = s.Vat15Subtotal,
                Vat5Subtotal = s.Vat5Subtotal,
                Vat0Subtotal = s.Vat0Subtotal,
                VatExemptSubtotal = s.VatExemptSubtotal,
                VatNotSubjectSubtotal = s.VatNotSubjectSubtotal,
                Total = s.Total,
                Notes = s.Notes,
                CompanyId = s.CompanyId,
                EstablishmentId = s.EstablishmentId,
                EmissionPointId = s.EmissionPointId,
                UserId = s.UserId,
                CreatedAt = s.CreatedAt,
                Items = s.Items
                    .OrderBy(i => i.Id)
                    .Select(i => new SaleItemDto
                    {
                        Id = i.Id,
                        ProductId = i.ProductId,
                        ProductName = i.Product.Name,
                        Quantity = i.Quantity,
                        UnitPrice = i.UnitPrice,
                        GrossSubtotal = i.GrossSubtotal,
                        DiscountAmount = i.DiscountAmount,
                        NetSubtotal = i.NetSubtotal,
                        LineSubtotal = i.LineSubtotal,
                        VatCategory = i.VatCategory,
                        VatRate = i.VatRate,
                        TaxableSubtotal = i.TaxableSubtotal,
                        TaxAmount = i.TaxAmount,
                        LineTotal = i.LineTotal
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync();
    }

    public async Task<string?> GetSriXmlDraftAsync(int id)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        return await _context.Sales
            .AsNoTracking()
            .Where(s => s.Id == id
                && s.CompanyId == operationalContext.CompanyId
                && s.EstablishmentId == operationalContext.EstablishmentId
                && s.EmissionPointId == operationalContext.EmissionPointId
                && s.DocumentType == SaleDocumentType.Invoice
                && s.SriXmlDraft != null)
            .Select(s => s.SriXmlDraft)
            .FirstOrDefaultAsync();
    }

    public async Task<SaleDto> CreateAsync(SaleCreateDto dto)
    {
        OperationalContext? operationalContext = null;

        try
        {
            if (dto.Items is null || dto.Items.Count == 0)
            {
                throw new InvalidOperationException("SALE_ITEMS_REQUIRED");
            }

            operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

            var paymentMethod = dto.PaymentMethod ?? SalePaymentMethod.Cash;
            var documentType = dto.DocumentType ?? SaleDocumentType.Ticket;

            if (!Enum.IsDefined(paymentMethod))
            {
                throw new InvalidOperationException("INVALID_SALE_PAYMENT_METHOD");
            }

            if (!Enum.IsDefined(documentType))
            {
                throw new InvalidOperationException("INVALID_SALE_DOCUMENT_TYPE");
            }

            Customer? customer = null;

            if (dto.CustomerId.HasValue)
            {
                customer = await _context.Customers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c =>
                        c.Id == dto.CustomerId.Value
                        && c.CompanyId == operationalContext.CompanyId
                        && c.IsActive);

                if (customer is null)
                {
                    throw new KeyNotFoundException("CUSTOMER_NOT_FOUND");
                }
            }

            var itemProductIds = dto.Items
                .Select(i => i.ProductId)
                .Distinct()
                .ToList();

            var products = await _context.Products
                .AsNoTracking()
                .Where(p => itemProductIds.Contains(p.Id) && p.CompanyId == operationalContext.CompanyId)
                .ToDictionaryAsync(p => p.Id);

            foreach (var requestedProductId in itemProductIds)
            {
                if (!products.ContainsKey(requestedProductId))
                {
                    throw new KeyNotFoundException("PRODUCT_NOT_FOUND");
                }
            }

            foreach (var product in products.Values)
            {
                if (!product.IsActive)
                {
                    throw new InvalidOperationException("PRODUCT_INACTIVE");
                }
            }

            var sale = new Sale
            {
                CompanyId = operationalContext.CompanyId,
                EstablishmentId = operationalContext.EstablishmentId,
                EmissionPointId = operationalContext.EmissionPointId,
                UserId = operationalContext.UserId,
                CustomerId = customer?.Id,
                Status = SaleStatus.Completed,
                PaymentMethod = paymentMethod,
                DocumentType = documentType,
                Notes = dto.Notes?.Trim(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            foreach (var itemDto in dto.Items.OrderBy(i => i.ProductId))
            {
                if (itemDto.Quantity <= 0m)
                {
                    throw new InvalidOperationException("INVALID_QUANTITY");
                }

                if (itemDto.UnitPrice < 0m)
                {
                    throw new InvalidOperationException("INVALID_UNIT_PRICE");
                }

                var product = products[itemDto.ProductId];
                var grossSubtotal = RoundMoney(itemDto.Quantity * itemDto.UnitPrice);
                var lineDiscount = RoundMoney(itemDto.DiscountAmount ?? 0m);

                if (lineDiscount < 0m || lineDiscount > grossSubtotal)
                {
                    throw new InvalidOperationException("INVALID_LINE_DISCOUNT");
                }

                var taxSnapshot = CalculateLineTax(grossSubtotal, lineDiscount, product.VatCategory);

                sale.Items.Add(new SaleItem
                {
                    ProductId = itemDto.ProductId,
                    Quantity = itemDto.Quantity,
                    UnitPrice = itemDto.UnitPrice,
                    GrossSubtotal = grossSubtotal,
                    DiscountAmount = lineDiscount,
                    NetSubtotal = taxSnapshot.TaxableSubtotal,
                    LineSubtotal = taxSnapshot.TaxableSubtotal,
                    VatCategory = product.VatCategory,
                    VatRate = taxSnapshot.VatRate,
                    TaxableSubtotal = taxSnapshot.TaxableSubtotal,
                    TaxAmount = taxSnapshot.TaxAmount,
                    LineTotal = taxSnapshot.LineTotal
                });
            }

            ApplySaleTaxTotals(sale, dto.DiscountAmount ?? 0m);

            await using var transaction = await _context.Database.BeginTransactionAsync();

            await AssignDocumentNumberAsync(sale, operationalContext, documentType);

            if (documentType == SaleDocumentType.Invoice)
            {
                await AssignSriInvoiceDraftAsync(sale, operationalContext, customer, products);
            }

            _context.Sales.Add(sale);
            await _context.SaveChangesAsync();

            foreach (var item in sale.Items.OrderBy(i => i.ProductId))
            {
                await _inventoryService.RegisterSaleAsync(
                    item.ProductId,
                    item.Quantity,
                    sale.Id,
                    item.Id,
                    sale.Notes);
            }

            await transaction.CommitAsync();

            _logger.LogInformation(
                "Sale created successfully. SaleId {SaleId} UserId {UserId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId} ItemsCount {ItemsCount} Total {Total}",
                sale.Id,
                operationalContext.UserId,
                operationalContext.CompanyId,
                operationalContext.EstablishmentId,
                operationalContext.EmissionPointId,
                sale.Items.Count,
                sale.Total);

            var created = await GetByIdAsync(sale.Id);
            return created ?? throw new KeyNotFoundException("SALE_NOT_FOUND");
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning(
                ex,
                "Sale creation failed. ErrorCode {ErrorCode} UserId {UserId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId} ItemsCount {ItemsCount}",
                ex.Message,
                operationalContext?.UserId,
                operationalContext?.CompanyId,
                operationalContext?.EstablishmentId,
                operationalContext?.EmissionPointId,
                dto.Items?.Count ?? 0);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error creating sale. UserId {UserId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId} ItemsCount {ItemsCount}",
                operationalContext?.UserId,
                operationalContext?.CompanyId,
                operationalContext?.EstablishmentId,
                operationalContext?.EmissionPointId,
                dto.Items?.Count ?? 0);
            throw;
        }
    }

    public async Task<SaleDto> VoidAsync(int id, VoidSaleDto dto)
    {
        OperationalContext? operationalContext = null;

        try
        {
            operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

            await using var transaction = await _context.Database.BeginTransactionAsync();

            var sale = await _context.Sales
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""Sales""
                    WHERE ""Id"" = {id}
                      AND ""CompanyId"" = {operationalContext.CompanyId}
                      AND ""EstablishmentId"" = {operationalContext.EstablishmentId}
                      AND ""EmissionPointId"" = {operationalContext.EmissionPointId}
                    FOR UPDATE")
                .SingleOrDefaultAsync();

            if (sale is null)
            {
                throw new KeyNotFoundException("SALE_NOT_FOUND");
            }

            await _context.Entry(sale)
                .Collection(s => s.Items)
                .LoadAsync();

            if (sale.Status == SaleStatus.Voided)
            {
                throw new InvalidOperationException("SALE_ALREADY_VOIDED");
            }

            if (sale.Status != SaleStatus.Completed)
            {
                throw new InvalidOperationException("SALE_NOT_VOIDABLE");
            }

            var voidNotes = string.IsNullOrWhiteSpace(dto.Reason)
                ? "Void sale"
                : dto.Reason.Trim();

            foreach (var item in sale.Items.OrderBy(i => i.ProductId))
            {
                await _inventoryService.RegisterVoidAsync(
                    item.ProductId,
                    item.Quantity,
                    sale.Id,
                    item.Id,
                    voidNotes);
            }

            sale.Status = SaleStatus.Voided;
            sale.VoidedAt = DateTime.UtcNow;
            sale.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(dto.Reason))
            {
                sale.Notes = string.IsNullOrWhiteSpace(sale.Notes)
                    ? $"VOID: {voidNotes}"
                    : $"{sale.Notes} | VOID: {voidNotes}";
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation(
                "Sale voided successfully. SaleId {SaleId} UserId {UserId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId}",
                sale.Id,
                operationalContext.UserId,
                operationalContext.CompanyId,
                operationalContext.EstablishmentId,
                operationalContext.EmissionPointId);

            var response = await GetByIdAsync(sale.Id);
            return response ?? throw new KeyNotFoundException("SALE_NOT_FOUND");
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning(
                ex,
                "Sale void failed. SaleId {SaleId} ErrorCode {ErrorCode} UserId {UserId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId}",
                id,
                ex.Message,
                operationalContext?.UserId,
                operationalContext?.CompanyId,
                operationalContext?.EstablishmentId,
                operationalContext?.EmissionPointId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error voiding sale. SaleId {SaleId} UserId {UserId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId}",
                id,
                operationalContext?.UserId,
                operationalContext?.CompanyId,
                operationalContext?.EstablishmentId,
                operationalContext?.EmissionPointId);
            throw;
        }
    }

    private static void ApplySaleTaxTotals(Sale sale, decimal saleDiscountAmount)
    {
        var lineNetSubtotal = sale.Items.Sum(i => i.NetSubtotal);
        var globalDiscount = RoundMoney(saleDiscountAmount);

        if (globalDiscount < 0m || globalDiscount > lineNetSubtotal)
        {
            throw new InvalidOperationException("INVALID_SALE_DISCOUNT");
        }

        sale.GrossSubtotal = sale.Items.Sum(i => i.GrossSubtotal);
        sale.DiscountAmount = sale.Items.Sum(i => i.DiscountAmount) + globalDiscount;

        if (globalDiscount > 0m)
        {
            ApplyGlobalDiscountToLines(sale.Items, lineNetSubtotal, globalDiscount);
        }

        sale.Subtotal = sale.Items.Sum(i => i.TaxableSubtotal);
        sale.TaxAmount = sale.Items.Sum(i => i.TaxAmount);
        sale.Vat15Subtotal = sale.Items
            .Where(i => i.VatCategory == ProductVatCategory.Vat15)
            .Sum(i => i.TaxableSubtotal);
        sale.Vat5Subtotal = sale.Items
            .Where(i => i.VatCategory == ProductVatCategory.Vat5)
            .Sum(i => i.TaxableSubtotal);
        sale.Vat0Subtotal = sale.Items
            .Where(i => i.VatCategory == ProductVatCategory.Vat0)
            .Sum(i => i.TaxableSubtotal);
        sale.VatExemptSubtotal = sale.Items
            .Where(i => i.VatCategory == ProductVatCategory.VatExempt)
            .Sum(i => i.TaxableSubtotal);
        sale.VatNotSubjectSubtotal = sale.Items
            .Where(i => i.VatCategory == ProductVatCategory.VatNotSubject)
            .Sum(i => i.TaxableSubtotal);
        sale.Total = sale.Items.Sum(i => i.LineTotal);
    }

    private async Task AssignDocumentNumberAsync(
        Sale sale,
        OperationalContext operationalContext,
        SaleDocumentType documentType)
    {
        if (!Enum.IsDefined(documentType))
        {
            throw new InvalidOperationException("INVALID_DOCUMENT_TYPE");
        }

        var documentContext = await _context.Establishments
            .AsNoTracking()
            .Where(e =>
                e.Id == operationalContext.EstablishmentId
                && e.CompanyId == operationalContext.CompanyId
                && e.IsActive)
            .Select(e => new
            {
                EstablishmentCode = e.Code,
                EmissionPointCode = e.EmissionPoints
                    .Where(ep => ep.Id == operationalContext.EmissionPointId && ep.IsActive)
                    .Select(ep => ep.Code)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        if (documentContext is null || documentContext.EmissionPointCode is null)
        {
            throw new InvalidOperationException("DOCUMENT_NUMBER_GENERATION_FAILED");
        }

        var establishmentCode = documentContext.EstablishmentCode.Trim();
        var emissionPointCode = documentContext.EmissionPointCode.Trim();

        if (establishmentCode.Length != 3 || emissionPointCode.Length != 3)
        {
            throw new InvalidOperationException("DOCUMENT_NUMBER_GENERATION_FAILED");
        }

        var now = DateTime.UtcNow;
        var documentTypeValue = (int)documentType;

        try
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""DocumentSequences""
                    (""CompanyId"", ""EstablishmentId"", ""EmissionPointId"", ""DocumentType"", ""CurrentNumber"", ""CreatedAt"", ""UpdatedAt"")
                VALUES
                    ({operationalContext.CompanyId}, {operationalContext.EstablishmentId}, {operationalContext.EmissionPointId}, {documentTypeValue}, 0, {now}, {now})
                ON CONFLICT (""CompanyId"", ""EstablishmentId"", ""EmissionPointId"", ""DocumentType"") DO NOTHING");

            var sequence = await _context.DocumentSequences
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""DocumentSequences""
                    WHERE ""CompanyId"" = {operationalContext.CompanyId}
                      AND ""EstablishmentId"" = {operationalContext.EstablishmentId}
                      AND ""EmissionPointId"" = {operationalContext.EmissionPointId}
                      AND ""DocumentType"" = {documentTypeValue}
                    FOR UPDATE")
                .SingleOrDefaultAsync();

            if (sequence is null)
            {
                throw new InvalidOperationException("DOCUMENT_SEQUENCE_ERROR");
            }

            sequence.CurrentNumber += 1;
            sequence.UpdatedAt = now;

            sale.Number = $"{establishmentCode}-{emissionPointCode}-{sequence.CurrentNumber:000000000}";
            sale.EstablishmentCodeSnapshot = establishmentCode;
            sale.EmissionPointCodeSnapshot = emissionPointCode;
            sale.Sequential = sequence.CurrentNumber;
            sale.DocumentIssuedAt = now;
            sale.DocumentStatus = documentType == SaleDocumentType.Invoice
                ? SaleDocumentStatus.Draft
                : SaleDocumentStatus.NotRequired;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Document number generation failed. CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId} DocumentType {DocumentType}",
                operationalContext.CompanyId,
                operationalContext.EstablishmentId,
                operationalContext.EmissionPointId,
                documentType);

            throw new InvalidOperationException("DOCUMENT_NUMBER_GENERATION_FAILED", ex);
        }
    }

    private async Task AssignSriInvoiceDraftAsync(
        Sale sale,
        OperationalContext operationalContext,
        Customer? customer,
        IReadOnlyDictionary<int, Product> products)
    {
        if (sale.DocumentType != SaleDocumentType.Invoice)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(sale.Number)
            || string.IsNullOrWhiteSpace(sale.EstablishmentCodeSnapshot)
            || string.IsNullOrWhiteSpace(sale.EmissionPointCodeSnapshot)
            || sale.Sequential is null
            || sale.DocumentIssuedAt is null
            || !IsNumeric(sale.EstablishmentCodeSnapshot, 3)
            || !IsNumeric(sale.EmissionPointCodeSnapshot, 3))
        {
            throw new InvalidOperationException("INVALID_SRI_DOCUMENT_CONTEXT");
        }

        if (customer is not null && string.IsNullOrWhiteSpace(customer.Identification))
        {
            throw new InvalidOperationException("INVALID_SRI_CUSTOMER_IDENTIFICATION");
        }

        var fiscalContext = await _context.Establishments
            .AsNoTracking()
            .Where(e =>
                e.Id == operationalContext.EstablishmentId
                && e.CompanyId == operationalContext.CompanyId
                && e.IsActive)
            .Select(e => new
            {
                Company = e.Company,
                Establishment = e
            })
            .FirstOrDefaultAsync();

        if (fiscalContext is null)
        {
            throw new InvalidOperationException("INVALID_SRI_DOCUMENT_CONTEXT");
        }

        var issuerRuc = fiscalContext.Company.Ruc?.Trim();

        if (!IsNumeric(issuerRuc, 13))
        {
            throw new InvalidOperationException("INVALID_ISSUER_RUC");
        }

        try
        {
            var sriSettings = await ResolveSriSettingsAsync(operationalContext.CompanyId);
            var accessKey = _sriAccessKeyService.GenerateInvoiceAccessKey(new SriAccessKeyRequest
            {
                EmissionDate = sale.DocumentIssuedAt.Value,
                DocumentCode = "01",
                IssuerRuc = issuerRuc!,
                Environment = sriSettings.Environment,
                EstablishmentCode = sale.EstablishmentCodeSnapshot,
                EmissionPointCode = sale.EmissionPointCodeSnapshot,
                Sequential = sale.Sequential.Value,
                EmissionType = sriSettings.EmissionType,
                NumericCodeSeed = string.Join(
                    "-",
                    operationalContext.CompanyId,
                    operationalContext.EstablishmentId,
                    operationalContext.EmissionPointId,
                    (int)sale.DocumentType,
                    sale.Sequential.Value)
            });

            sale.AccessKey = accessKey.AccessKey;
            sale.SriEnvironment = accessKey.Environment;
            sale.SriEmissionType = accessKey.EmissionType;
            sale.SriNumericCode = accessKey.NumericCode;

            var xmlDraft = _sriXmlDraftService.GenerateInvoiceXmlDraft(new SriXmlDraftRequest
            {
                Sale = sale,
                Company = fiscalContext.Company,
                Establishment = fiscalContext.Establishment,
                Customer = customer,
                Products = products.ToDictionary(
                    p => p.Key,
                    p => new SriXmlProductSnapshot
                    {
                        ProductId = p.Value.Id,
                        Name = p.Value.Name,
                        Barcode = p.Value.Barcode,
                        InternalCode = p.Value.InternalCode
                    }),
                Environment = sriSettings.Environment,
                EmissionType = sriSettings.EmissionType
            });

            _sriInvoiceXmlValidator.ValidateUnsignedInvoiceXml(xmlDraft);
            sale.SriXmlDraft = xmlDraft;
            sale.SriXmlGeneratedAt = DateTime.UtcNow;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "SRI invoice draft generation failed. CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId} Sequential {Sequential}",
                operationalContext.CompanyId,
                operationalContext.EstablishmentId,
                operationalContext.EmissionPointId,
                sale.Sequential);

            throw new InvalidOperationException("SRI_XML_DRAFT_GENERATION_FAILED", ex);
        }
    }

    private static void ApplyGlobalDiscountToLines(
        ICollection<SaleItem> items,
        decimal lineNetSubtotal,
        decimal globalDiscount)
    {
        var orderedItems = items.OrderBy(i => i.ProductId).ToList();
        var allocatedDiscount = 0m;

        for (var index = 0; index < orderedItems.Count; index++)
        {
            var item = orderedItems[index];
            var allocation = index == orderedItems.Count - 1
                ? globalDiscount - allocatedDiscount
                : RoundMoney(globalDiscount * item.NetSubtotal / lineNetSubtotal);

            allocatedDiscount += allocation;

            item.DiscountAmount = RoundMoney(item.DiscountAmount + allocation);
            item.NetSubtotal = RoundMoney(item.NetSubtotal - allocation);
            item.LineSubtotal = item.NetSubtotal;
            item.TaxableSubtotal = item.NetSubtotal;
            item.TaxAmount = RoundMoney(item.TaxableSubtotal * item.VatRate);
            item.LineTotal = item.TaxableSubtotal + item.TaxAmount;
        }
    }

    private async Task<(int Environment, int EmissionType)> ResolveSriSettingsAsync(int companyId)
    {
        var settings = await _context.CompanySriSettings
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId)
            .Select(s => new { s.Environment, s.EmissionType })
            .FirstOrDefaultAsync();

        return settings is null
            ? (_sriOptions.Environment, _sriOptions.EmissionType)
            : (settings.Environment, settings.EmissionType);
    }

    private static (decimal VatRate, decimal TaxableSubtotal, decimal TaxAmount, decimal LineTotal) CalculateLineTax(
        decimal grossSubtotal,
        decimal discountAmount,
        ProductVatCategory vatCategory)
    {
        var taxableSubtotal = RoundMoney(grossSubtotal - discountAmount);
        var vatRate = GetVatRate(vatCategory);
        var taxAmount = RoundMoney(taxableSubtotal * vatRate);
        var lineTotal = taxableSubtotal + taxAmount;

        return (vatRate, taxableSubtotal, taxAmount, lineTotal);
    }

    private static decimal GetVatRate(ProductVatCategory vatCategory)
        => vatCategory switch
        {
            ProductVatCategory.Vat15 => 0.15m,
            ProductVatCategory.Vat5 => 0.05m,
            ProductVatCategory.Vat0 => 0.00m,
            ProductVatCategory.VatExempt => 0.00m,
            ProductVatCategory.VatNotSubject => 0.00m,
            _ => throw new InvalidOperationException("INVALID_PRODUCT_VAT_CATEGORY")
        };

    private static decimal RoundMoney(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static bool IsNumeric(string? value, int expectedLength)
    {
        return value is not null
            && value.Length == expectedLength
            && value.All(char.IsDigit);
    }
}
