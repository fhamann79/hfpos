using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class PurchaseReceiptQueryService : IPurchaseReceiptQueryService
{
    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;

    public PurchaseReceiptQueryService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
    }

    public async Task<PurchaseReceiptListResultDto> GetListAsync(PurchaseReceiptListQueryDto request)
    {
        request ??= new PurchaseReceiptListQueryDto();
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var query = _context.PurchaseReceipts
            .AsNoTracking()
            .Where(r => r.CompanyId == operationalContext.CompanyId
                && r.EstablishmentId == operationalContext.EstablishmentId);

        if (request.From.HasValue)
        {
            var fromDate = DateOnly.FromDateTime(request.From.Value);
            query = query.Where(r => r.ReceiptBusinessDate >= fromDate);
        }

        if (request.To.HasValue)
        {
            var toDate = DateOnly.FromDateTime(request.To.Value);
            query = query.Where(r => r.ReceiptBusinessDate <= toDate);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(r => r.Status == request.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(r =>
                r.Supplier.Name.ToLower().Contains(term)
                || (r.ReceiptNumber != null && r.ReceiptNumber.ToLower().Contains(term))
                || (r.SupplierDocumentNumber != null && r.SupplierDocumentNumber.ToLower().Contains(term))
                || (r.Notes != null && r.Notes.ToLower().Contains(term)));
        }

        var aggregate = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalItems = g.Count(),
                PostedCount = g.Count(r => r.Status == PurchaseReceiptStatus.Posted),
                CanceledCount = g.Count(r => r.Status == PurchaseReceiptStatus.Canceled),
                TotalReceived = g.Sum(r => r.Status == PurchaseReceiptStatus.Posted ? r.Subtotal : 0m)
            })
            .SingleOrDefaultAsync();
        var totalItems = aggregate?.TotalItems ?? 0;

        var items = await query
            .OrderByDescending(r => r.ReceiptBusinessDate)
            .ThenByDescending(r => r.ReceiptDate)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new PurchaseReceiptListItemDto
            {
                Id = r.Id,
                SupplierId = r.SupplierId,
                SupplierName = r.Supplier.Name,
                ReceiptNumber = r.ReceiptNumber,
                SupplierDocumentNumber = r.SupplierDocumentNumber,
                ReceiptDate = r.ReceiptDate,
                ReceiptBusinessDate = r.ReceiptBusinessDate,
                ReceiptTimeZoneIdSnapshot = r.ReceiptTimeZoneIdSnapshot,
                Status = r.Status,
                Subtotal = r.Subtotal,
                Notes = r.Notes,
                CreatedAt = r.CreatedAt,
                CreatedByUserId = r.CreatedByUserId,
                CreatedByUsername = r.CreatedByUser.Username,
                PostedAt = r.PostedAt,
                CanceledAt = r.CanceledAt,
                CanceledBusinessDate = r.CanceledBusinessDate,
                CanceledTimeZoneIdSnapshot = r.CanceledTimeZoneIdSnapshot,
                CanceledByUserId = r.CanceledByUserId,
                CanceledByUsername = r.CanceledByUser != null ? r.CanceledByUser.Username : null,
                CancelReason = r.CancelReason
            })
            .ToListAsync();

        return new PurchaseReceiptListResultDto
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize),
            Summary = new PurchaseReceiptSummaryDto
            {
                PostedCount = aggregate?.PostedCount ?? 0,
                CanceledCount = aggregate?.CanceledCount ?? 0,
                TotalReceived = aggregate?.TotalReceived ?? 0m
            }
        };
    }
}
