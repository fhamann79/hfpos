using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class ElectronicDocumentCenterTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    private static readonly DateTime SharedInstant =
        new(2026, 9, 20, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly SharedBusinessDate = new(2026, 9, 20);

    public Task InitializeAsync() => database.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Mixed_documents_are_sql_paged_stable_filtered_and_summarized()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "documents", 301, 20m);
        var otherTenant = await TestDataBuilder.CreateTenantAsync(database, "documents-other", 302, 20m);
        var seeded = await SeedDocumentSetAsync(tenant, "DOC", SharedBusinessDate, SharedInstant);
        await SeedDocumentSetAsync(otherTenant, "OTHER", SharedBusinessDate, SharedInstant);

        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var first = await services.ElectronicDocuments.GetListAsync(new ElectronicDocumentQueryDto
        {
            Page = 1,
            PageSize = 3
        });
        var second = await services.ElectronicDocuments.GetListAsync(new ElectronicDocumentQueryDto
        {
            Page = 2,
            PageSize = 3
        });
        var third = await services.ElectronicDocuments.GetListAsync(new ElectronicDocumentQueryDto
        {
            Page = 3,
            PageSize = 3
        });

        var actualKeys = first.Items.Concat(second.Items).Concat(third.Items)
            .Select(item => (item.Kind, item.Id))
            .ToArray();
        var expectedKeys = seeded.Invoices
            .OrderByDescending(invoice => invoice.Id)
            .Select(invoice => (ElectronicDocumentKind.Invoice, invoice.Id))
            .Concat(seeded.CreditNotes
                .OrderByDescending(note => note.Id)
                .Select(note => (ElectronicDocumentKind.CreditNote, note.Id)))
            .ToArray();

        Assert.Equal(8, first.TotalItems);
        Assert.Equal(3, first.TotalPages);
        Assert.Equal(expectedKeys, actualKeys);
        Assert.Equal(actualKeys.Length, actualKeys.Distinct().Count());
        Assert.DoesNotContain(first.Items, item => item.Number == seeded.Ticket.Number);
        Assert.Equal(first.Summary.TotalDocuments, second.Summary.TotalDocuments);
        Assert.Equal(first.Summary.AuthorizedCount, third.Summary.AuthorizedCount);
        Assert.Equal(5, first.Summary.InvoiceCount);
        Assert.Equal(3, first.Summary.CreditNoteCount);
        Assert.Equal(2, first.Summary.AuthorizedCount);
        Assert.Equal(2, first.Summary.PendingAuthorizationCount);
        Assert.Equal(1, first.Summary.RejectedCount);
        Assert.Equal(1, first.Summary.CancelledCount);
        Assert.Equal(2, first.Summary.WithSriErrorCount);

        var sorted = await services.ElectronicDocuments.GetListAsync(new ElectronicDocumentQueryDto
        {
            SortField = "total",
            SortOrder = "asc",
            PageSize = 50
        });
        var expectedSortedKeys = seeded.Invoices
            .Select(invoice => (Kind: ElectronicDocumentKind.Invoice, invoice.Id, invoice.Total))
            .Concat(seeded.CreditNotes.Select(note =>
                (Kind: ElectronicDocumentKind.CreditNote, note.Id, note.Total)))
            .OrderBy(item => item.Total)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.Id)
            .Select(item => (item.Kind, item.Id));
        Assert.Equal(expectedSortedKeys, sorted.Items.Select(item => (item.Kind, item.Id)));

        var invoices = await services.ElectronicDocuments.GetListAsync(new ElectronicDocumentQueryDto
        {
            Kind = ElectronicDocumentKind.Invoice,
            PageSize = 50
        });
        Assert.Equal(5, invoices.TotalItems);
        Assert.All(invoices.Items, item => Assert.Equal(ElectronicDocumentKind.Invoice, item.Kind));

        var authorized = await services.ElectronicDocuments.GetListAsync(new ElectronicDocumentQueryDto
        {
            DocumentStatus = SaleDocumentStatus.Authorized,
            PageSize = 50
        });
        Assert.Equal(2, authorized.TotalItems);
        Assert.Equal(2, authorized.Summary.AuthorizedCount);

        var bounded = await services.ElectronicDocuments.GetListAsync(new ElectronicDocumentQueryDto
        {
            Page = 0,
            PageSize = 999
        });
        Assert.Equal(1, bounded.Page);
        Assert.Equal(200, bounded.PageSize);
    }

    [Fact]
    public async Task Search_business_date_and_operational_context_apply_before_union()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "documents-search", 303, 20m);
        var seeded = await SeedDocumentSetAsync(tenant, "SEARCH", SharedBusinessDate, SharedInstant);
        await SeedOtherOperationalContextAsync(tenant);

        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var terms = new[]
        {
            seeded.Invoices[0].Number!,
            seeded.Invoices[0].AccessKey!,
            seeded.Invoices[0].AuthorizationNumber!,
            seeded.Invoices[0].BuyerIdentificationSnapshot!,
            seeded.CreditNotes[0].Number!,
            seeded.CreditNotes[0].OriginalSaleNumberSnapshot!,
            seeded.CreditNotes[0].AccessKey!,
            seeded.CreditNotes[0].BuyerIdentificationSnapshot!
        };

        foreach (var term in terms)
        {
            var result = await services.ElectronicDocuments.GetListAsync(new ElectronicDocumentQueryDto
            {
                Search = term,
                PageSize = 50
            });
            Assert.NotEmpty(result.Items);
            Assert.DoesNotContain(result.Items, item => item.Number == "CONTEXT-OTHER");
        }

        var includedDate = await services.ElectronicDocuments.GetListAsync(new ElectronicDocumentQueryDto
        {
            From = SharedBusinessDate.ToDateTime(TimeOnly.MinValue),
            To = SharedBusinessDate.ToDateTime(TimeOnly.MinValue),
            PageSize = 50
        });
        var excludedDate = await services.ElectronicDocuments.GetListAsync(new ElectronicDocumentQueryDto
        {
            From = SharedBusinessDate.AddDays(1).ToDateTime(TimeOnly.MinValue),
            PageSize = 50
        });
        var sriErrors = await services.ElectronicDocuments.GetListAsync(new ElectronicDocumentQueryDto
        {
            OnlyWithSriError = true,
            PageSize = 50
        });

        Assert.Equal(8, includedDate.TotalItems);
        Assert.Equal(0, excludedDate.TotalItems);
        Assert.Equal(2, sriErrors.TotalItems);
        Assert.All(sriErrors.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.SriLastSubmissionError)));
    }

    [Fact]
    public async Task Detail_is_tenant_scoped_excludes_tickets_and_never_exposes_xml_or_collections()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "documents-detail", 304, 20m);
        var otherTenant = await TestDataBuilder.CreateTenantAsync(database, "documents-detail-other", 305, 20m);
        var seeded = await SeedDocumentSetAsync(tenant, "DETAIL", SharedBusinessDate, SharedInstant);
        var other = await SeedDocumentSetAsync(otherTenant, "DETAIL-OTHER", SharedBusinessDate, SharedInstant);

        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var invoice = await services.ElectronicDocuments.GetByIdAsync(
            ElectronicDocumentKind.Invoice,
            seeded.Invoices[0].Id);
        var creditNote = await services.ElectronicDocuments.GetByIdAsync(
            ElectronicDocumentKind.CreditNote,
            seeded.CreditNotes[0].Id);
        var ticket = await services.ElectronicDocuments.GetByIdAsync(
            ElectronicDocumentKind.Invoice,
            seeded.Ticket.Id);
        var crossTenant = await services.ElectronicDocuments.GetByIdAsync(
            ElectronicDocumentKind.Invoice,
            other.Invoices[0].Id);

        Assert.NotNull(invoice);
        Assert.Equal(ElectronicDocumentKind.Invoice, invoice.Kind);
        Assert.NotNull(creditNote);
        Assert.Equal(seeded.Invoices[0].Id, creditNote.OriginalSaleId);
        Assert.Null(ticket);
        Assert.Null(crossTenant);

        var listProperties = typeof(ElectronicDocumentListItemDto).GetProperties()
            .Select(property => property.Name)
            .ToArray();
        Assert.DoesNotContain("SriXmlDraft", listProperties);
        Assert.DoesNotContain("SriSignedXml", listProperties);
        Assert.DoesNotContain("Items", listProperties);
        Assert.DoesNotContain("SriSubmissionAttempts", listProperties);
        Assert.DoesNotContain("EmailDeliveries", listProperties);
    }

    [Fact]
    public async Task List_query_count_is_constant_regardless_of_page_rows()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "documents-count", 306, 20m);
        await SeedDocumentSetAsync(tenant, "COUNT", SharedBusinessDate, SharedInstant);
        var counter = new CommandCountingInterceptor();

        await using var services = new TestServiceScope(database, tenant.OperationalContext, counter);
        await services.ElectronicDocuments.GetListAsync(new ElectronicDocumentQueryDto
        {
            PageSize = 1
        });
        var oneRowQueries = counter.ReaderCount;

        counter.Reset();
        await services.ElectronicDocuments.GetListAsync(new ElectronicDocumentQueryDto
        {
            PageSize = 50
        });
        var manyRowQueries = counter.ReaderCount;

        Assert.Equal(2, oneRowQueries);
        Assert.Equal(oneRowQueries, manyRowQueries);
    }

    private async Task<SeededDocuments> SeedDocumentSetAsync(
        TestTenant tenant,
        string prefix,
        DateOnly businessDate,
        DateTime createdAt)
    {
        await using var context = database.CreateDbContext();
        var invoices = Enumerable.Range(1, 5)
            .Select(index => CreateSale(
                tenant,
                $"{prefix}-INV-{index:000}",
                SaleDocumentType.Invoice,
                index switch
                {
                    1 => SaleDocumentStatus.Authorized,
                    2 => SaleDocumentStatus.PendingAuthorization,
                    3 => SaleDocumentStatus.Rejected,
                    _ => SaleDocumentStatus.Draft
                },
                businessDate,
                createdAt,
                index))
            .ToList();
        var ticket = CreateSale(
            tenant,
            $"{prefix}-TICKET",
            SaleDocumentType.Ticket,
            SaleDocumentStatus.NotRequired,
            businessDate,
            createdAt,
            9);
        context.Sales.AddRange(invoices.Append(ticket));
        await context.SaveChangesAsync();

        var creditNotes = Enumerable.Range(1, 3)
            .Select(index => new CreditNote
            {
                CompanyId = tenant.CompanyId,
                EstablishmentId = tenant.EstablishmentId,
                EmissionPointId = tenant.EmissionPointId,
                UserId = tenant.UserId,
                OriginalSaleId = invoices[index - 1].Id,
                OriginalSaleNumberSnapshot = invoices[index - 1].Number,
                BuyerNameSnapshot = $"Credit buyer {prefix} {index}",
                BuyerIdentificationSnapshot = $"CN-{tenant.CompanyId}-{index}",
                BuyerEmailSnapshot = $"credit-{prefix}-{index}@hfpos.test",
                DocumentStatus = index switch
                {
                    1 => SaleDocumentStatus.Authorized,
                    2 => SaleDocumentStatus.PendingAuthorization,
                    _ => SaleDocumentStatus.Cancelled
                },
                Number = $"{prefix}-NC-{index:000}",
                DocumentIssuedAt = createdAt,
                AccessKey = $"CN-ACCESS-{prefix}-{index}",
                AuthorizationNumber = index == 1 ? $"CN-AUTH-{prefix}-{index}" : null,
                AuthorizedAt = index == 1 ? createdAt : null,
                SriEnvironment = 1,
                SriXmlDraft = $"<notaCredito>{new string('x', 2000)}</notaCredito>",
                SriSignedXml = index == 1 ? "<signed />" : null,
                SriSubmittedAt = index <= 2 ? createdAt : null,
                SriReceptionStatus = index <= 2 ? "RECIBIDA" : null,
                SriAuthorizationStatus = index == 1 ? "AUTORIZADO" : null,
                SriLastSubmissionError = index == 2 ? "CREDIT_NOTE_TEST_ERROR" : null,
                Reason = $"Reason {prefix} {index}",
                GrossSubtotal = 10m * index,
                Subtotal = 10m * index,
                Vat0Subtotal = 10m * index,
                Total = 10m * index,
                BusinessDate = businessDate,
                TimeZoneIdSnapshot = tenant.OperationalContext.CompanyTimeZoneId,
                CreatedAt = createdAt,
                VoidedAt = index == 3 ? createdAt : null
            })
            .ToList();
        context.CreditNotes.AddRange(creditNotes);
        await context.SaveChangesAsync();

        return new SeededDocuments(invoices, ticket, creditNotes);
    }

    private static Sale CreateSale(
        TestTenant tenant,
        string number,
        SaleDocumentType documentType,
        SaleDocumentStatus documentStatus,
        DateOnly businessDate,
        DateTime createdAt,
        int index)
        => new()
        {
            CompanyId = tenant.CompanyId,
            EstablishmentId = tenant.EstablishmentId,
            EmissionPointId = tenant.EmissionPointId,
            UserId = tenant.UserId,
            Status = SaleStatus.Completed,
            PaymentMethod = SalePaymentMethod.Cash,
            DocumentType = documentType,
            DocumentStatus = documentStatus,
            Number = number,
            BuyerNameSnapshot = $"Invoice buyer {number}",
            BuyerIdentificationSnapshot = $"INV-{tenant.CompanyId}-{index}",
            BuyerEmailSnapshot = $"invoice-{index}@hfpos.test",
            DocumentIssuedAt = createdAt,
            AccessKey = $"INV-ACCESS-{number}",
            AuthorizationNumber = documentStatus == SaleDocumentStatus.Authorized ? $"INV-AUTH-{number}" : null,
            AuthorizedAt = documentStatus == SaleDocumentStatus.Authorized ? createdAt : null,
            SriEnvironment = 1,
            SriXmlDraft = $"<factura>{new string('x', 2000)}</factura>",
            SriSignedXml = documentStatus == SaleDocumentStatus.Authorized ? "<signed />" : null,
            SriSubmittedAt = documentStatus is SaleDocumentStatus.Authorized or SaleDocumentStatus.PendingAuthorization
                ? createdAt
                : null,
            SriReceptionStatus = documentStatus is SaleDocumentStatus.Authorized or SaleDocumentStatus.PendingAuthorization
                ? "RECIBIDA"
                : null,
            SriAuthorizationStatus = documentStatus == SaleDocumentStatus.Authorized ? "AUTORIZADO" : null,
            SriLastSubmissionError = documentStatus == SaleDocumentStatus.Rejected ? "INVOICE_TEST_ERROR" : null,
            GrossSubtotal = 12m * index,
            Subtotal = 12m * index,
            Vat0Subtotal = 12m * index,
            Total = 12m * index,
            BusinessDate = businessDate,
            TimeZoneIdSnapshot = tenant.OperationalContext.CompanyTimeZoneId,
            CreatedAt = createdAt
        };

    private async Task SeedOtherOperationalContextAsync(TestTenant tenant)
    {
        await using var context = database.CreateDbContext();
        var establishment = new Establishment
        {
            CompanyId = tenant.CompanyId,
            Code = "002",
            Name = "Other establishment",
            Address = "Other address",
            IsActive = true,
            CreatedAt = SharedInstant
        };
        context.Establishments.Add(establishment);
        await context.SaveChangesAsync();

        var emissionPoint = new EmissionPoint
        {
            EstablishmentId = establishment.Id,
            Code = "002",
            Name = "Other emission point",
            IsActive = true,
            CreatedAt = SharedInstant
        };
        context.EmissionPoints.Add(emissionPoint);
        await context.SaveChangesAsync();

        context.Sales.Add(new Sale
        {
            CompanyId = tenant.CompanyId,
            EstablishmentId = establishment.Id,
            EmissionPointId = emissionPoint.Id,
            UserId = tenant.UserId,
            Status = SaleStatus.Completed,
            PaymentMethod = SalePaymentMethod.Cash,
            DocumentType = SaleDocumentType.Invoice,
            DocumentStatus = SaleDocumentStatus.Draft,
            Number = "CONTEXT-OTHER",
            GrossSubtotal = 1m,
            Subtotal = 1m,
            Vat0Subtotal = 1m,
            Total = 1m,
            BusinessDate = SharedBusinessDate,
            TimeZoneIdSnapshot = tenant.OperationalContext.CompanyTimeZoneId,
            CreatedAt = SharedInstant
        });
        await context.SaveChangesAsync();
    }

    private sealed record SeededDocuments(
        IReadOnlyList<Sale> Invoices,
        Sale Ticket,
        IReadOnlyList<CreditNote> CreditNotes);
}
