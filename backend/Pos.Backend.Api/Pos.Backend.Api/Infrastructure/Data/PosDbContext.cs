using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Infrastructure.Data;

public class PosDbContext : DbContext
{
    public PosDbContext(DbContextOptions<PosDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<Permission> Permissions { get; set; }
    public DbSet<RolePermission> RolePermissions { get; set; }
    public DbSet<Company> Companies { get; set; }
    public DbSet<CompanyBranding> CompanyBrandings { get; set; }
    public DbSet<CompanyEmailSettings> CompanyEmailSettings { get; set; }
    public DbSet<CompanySriSettings> CompanySriSettings { get; set; }
    public DbSet<CompanySriCertificate> CompanySriCertificates { get; set; }
    public DbSet<Establishment> Establishments { get; set; }
    public DbSet<EmissionPoint> EmissionPoints { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<Customer> Customers { get; set; }
    public DbSet<ProductStock> ProductStocks { get; set; }
    public DbSet<InventoryMovement> InventoryMovements { get; set; }
    public DbSet<DocumentSequence> DocumentSequences { get; set; }
    public DbSet<DocumentSequenceAudit> DocumentSequenceAudits { get; set; }
    public DbSet<Sale> Sales { get; set; }
    public DbSet<SaleItem> SaleItems { get; set; }
    public DbSet<SriSubmissionAttempt> SriSubmissionAttempts { get; set; }
    public DbSet<SaleInvoiceEmailDelivery> SaleInvoiceEmailDeliveries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.HasIndex(p => p.Code).IsUnique();
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.HasKey(rp => new { rp.RoleId, rp.PermissionId });
            entity.HasIndex(rp => new { rp.RoleId, rp.PermissionId }).IsUnique();
            entity.HasOne(rp => rp.Role)
                .WithMany(r => r.RolePermissions)
                .HasForeignKey(rp => rp.RoleId);
            entity.HasOne(rp => rp.Permission)
                .WithMany(p => p.RolePermissions)
                .HasForeignKey(rp => rp.PermissionId);
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.Property(c => c.TradeName)
                .HasMaxLength(150);

            entity.Property(c => c.MatrixAddress)
                .HasMaxLength(250);

            entity.Property(c => c.Email)
                .HasMaxLength(150);

            entity.Property(c => c.Phone)
                .HasMaxLength(30);

            entity.Property(c => c.SpecialTaxpayerNumber)
                .HasMaxLength(50);

            entity.Property(c => c.TaxpayerRegime)
                .HasMaxLength(80);
        });

        modelBuilder.Entity<CompanyBranding>(entity =>
        {
            entity.Property(b => b.LogoBytes)
                .HasColumnType("bytea");

            entity.Property(b => b.LogoContentType)
                .HasMaxLength(100);

            entity.Property(b => b.LogoFileName)
                .HasMaxLength(255);

            entity.Property(b => b.PrimaryColor)
                .HasMaxLength(20);

            entity.Property(b => b.DocumentFooterText)
                .HasMaxLength(500);

            entity.HasIndex(b => b.CompanyId)
                .IsUnique();

            entity.HasOne(b => b.Company)
                .WithOne(c => c.Branding)
                .HasForeignKey<CompanyBranding>(b => b.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(b => b.UpdatedByUser)
                .WithMany()
                .HasForeignKey(b => b.UpdatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CompanyEmailSettings>(entity =>
        {
            entity.Property(s => s.SmtpHost)
                .HasMaxLength(255);

            entity.Property(s => s.EncryptionMode)
                .IsRequired()
                .HasMaxLength(30)
                .HasDefaultValue("StartTls");

            entity.Property(s => s.SmtpUsername)
                .HasMaxLength(255);

            entity.Property(s => s.SmtpPasswordProtected)
                .HasColumnType("text");

            entity.Property(s => s.FromEmail)
                .HasMaxLength(320);

            entity.Property(s => s.FromDisplayName)
                .HasMaxLength(150);

            entity.Property(s => s.ReplyToEmail)
                .HasMaxLength(320);

            entity.Property(s => s.LastTestMessage)
                .HasMaxLength(500);

            entity.HasIndex(s => s.CompanyId)
                .IsUnique();

            entity.HasOne(s => s.Company)
                .WithOne(c => c.EmailSettings)
                .HasForeignKey<CompanyEmailSettings>(s => s.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(s => s.UpdatedByUser)
                .WithMany()
                .HasForeignKey(s => s.UpdatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CompanySriSettings>(entity =>
        {
            entity.HasIndex(s => s.CompanyId)
                .IsUnique();

            entity.HasOne(s => s.Company)
                .WithMany()
                .HasForeignKey(s => s.CompanyId);

            entity.HasOne(s => s.LastUpdatedByUser)
                .WithMany()
                .HasForeignKey(s => s.LastUpdatedByUserId);
        });

        modelBuilder.Entity<CompanySriCertificate>(entity =>
        {
            entity.Property(c => c.FileName)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(c => c.ContentType)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(c => c.EncryptedCertificateBytes)
                .IsRequired()
                .HasColumnType("bytea");

            entity.Property(c => c.EncryptedPassword)
                .IsRequired()
                .HasColumnType("bytea");

            entity.Property(c => c.Thumbprint)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(c => c.Subject)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(c => c.Issuer)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(c => c.SerialNumber)
                .IsRequired()
                .HasMaxLength(100);

            entity.HasIndex(c => c.CompanyId)
                .IsUnique()
                .HasFilter(@"""IsActive"" = true");

            entity.HasIndex(c => new { c.CompanyId, c.IsActive });

            entity.HasOne(c => c.Company)
                .WithMany()
                .HasForeignKey(c => c.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.UploadedByUser)
                .WithMany()
                .HasForeignKey(c => c.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.DeactivatedByUser)
                .WithMany()
                .HasForeignKey(c => c.DeactivatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });


        modelBuilder.Entity<Establishment>(entity =>
        {
            entity.HasIndex(e => new { e.CompanyId, e.Code }).IsUnique();
        });

        modelBuilder.Entity<EmissionPoint>(entity =>
        {
            entity.HasIndex(ep => new { ep.EstablishmentId, ep.Code }).IsUnique();
        });
        modelBuilder.Entity<Category>(entity =>
        {
            entity.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(150);

            entity.HasOne(c => c.Company)
                .WithMany()
                .HasForeignKey(c => c.CompanyId);

            entity.HasIndex(c => new { c.CompanyId, c.Name }).IsUnique();
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(c => c.Identification)
                .HasMaxLength(50);

            entity.Property(c => c.Phone)
                .HasMaxLength(30);

            entity.Property(c => c.Email)
                .HasMaxLength(320);

            entity.HasOne(c => c.Company)
                .WithMany()
                .HasForeignKey(c => c.CompanyId);

            entity.HasIndex(c => c.CompanyId);
            entity.HasIndex(c => new { c.CompanyId, c.Name });
        });



        modelBuilder.Entity<ProductStock>(entity =>
        {
            entity.Property(ps => ps.Quantity)
                .HasPrecision(18, 4);

            entity.HasIndex(ps => new { ps.ProductId, ps.CompanyId, ps.EstablishmentId })
                .IsUnique();

            entity.HasOne(ps => ps.Product)
                .WithMany()
                .HasForeignKey(ps => ps.ProductId);

            entity.HasOne(ps => ps.Company)
                .WithMany()
                .HasForeignKey(ps => ps.CompanyId);

            entity.HasOne(ps => ps.Establishment)
                .WithMany()
                .HasForeignKey(ps => ps.EstablishmentId);
        });

        modelBuilder.Entity<InventoryMovement>(entity =>
        {
            entity.Property(im => im.SourceType)
                .HasConversion<int>();

            entity.Property(im => im.Quantity)
                .HasPrecision(18, 4);

            entity.Property(im => im.StockBefore)
                .HasPrecision(18, 4);

            entity.Property(im => im.StockAfter)
                .HasPrecision(18, 4);

            entity.HasIndex(im => new { im.ProductId, im.CompanyId, im.EstablishmentId, im.CreatedAt });
            entity.HasIndex(im => new { im.CompanyId, im.EstablishmentId, im.CreatedAt });
            entity.HasIndex(im => new { im.CompanyId, im.EstablishmentId, im.ProductId, im.CreatedAt });
            entity.HasIndex(im => new { im.SourceType, im.SourceId });
            entity.HasIndex(im => new { im.SourceType, im.SourceId, im.SourceLineId })
                .IsUnique()
                .HasFilter(@"""SourceId"" IS NOT NULL AND ""SourceLineId"" IS NOT NULL AND ""SourceType"" IN (4, 5)");

            entity.HasOne(im => im.Product)
                .WithMany()
                .HasForeignKey(im => im.ProductId);

            entity.HasOne(im => im.Company)
                .WithMany()
                .HasForeignKey(im => im.CompanyId);

            entity.HasOne(im => im.Establishment)
                .WithMany()
                .HasForeignKey(im => im.EstablishmentId);

            entity.HasOne(im => im.User)
                .WithMany()
                .HasForeignKey(im => im.UserId);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(p => p.Barcode)
                .HasMaxLength(100);

            entity.Property(p => p.InternalCode)
                .HasMaxLength(100);

            entity.Property(p => p.Price)
                .HasPrecision(18, 2);

            entity.Property(p => p.MinimumStock)
                .HasPrecision(18, 4)
                .HasDefaultValue(3m);

            entity.Property(p => p.VatCategory)
                .HasConversion<int>()
                .HasDefaultValue(ProductVatCategory.Vat15);

            entity.HasOne(p => p.Company)
                .WithMany()
                .HasForeignKey(p => p.CompanyId);

            entity.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId);

            entity.HasIndex(p => p.CompanyId);
            entity.HasIndex(p => p.CategoryId);
            entity.HasIndex(p => new { p.CompanyId, p.Barcode })
                .IsUnique()
                .HasFilter(@"""Barcode"" IS NOT NULL");
            entity.HasIndex(p => new { p.CompanyId, p.InternalCode })
                .IsUnique()
                .HasFilter(@"""InternalCode"" IS NOT NULL");
        });

        modelBuilder.Entity<DocumentSequence>(entity =>
        {
            entity.Property(ds => ds.DocumentType)
                .HasConversion<int>();

            entity.HasIndex(ds => new { ds.CompanyId, ds.EstablishmentId, ds.EmissionPointId, ds.DocumentType })
                .IsUnique();

            entity.HasOne(ds => ds.Company)
                .WithMany()
                .HasForeignKey(ds => ds.CompanyId);

            entity.HasOne(ds => ds.Establishment)
                .WithMany()
                .HasForeignKey(ds => ds.EstablishmentId);

            entity.HasOne(ds => ds.EmissionPoint)
                .WithMany()
                .HasForeignKey(ds => ds.EmissionPointId);
        });

        modelBuilder.Entity<DocumentSequenceAudit>(entity =>
        {
            entity.Property(a => a.DocumentType)
                .HasConversion<int>();

            entity.Property(a => a.Reason)
                .IsRequired()
                .HasMaxLength(500);

            entity.HasIndex(a => new { a.CompanyId, a.CreatedAt });
            entity.HasIndex(a => new { a.DocumentSequenceId, a.CreatedAt });

            entity.HasOne(a => a.DocumentSequence)
                .WithMany()
                .HasForeignKey(a => a.DocumentSequenceId);

            entity.HasOne(a => a.Company)
                .WithMany()
                .HasForeignKey(a => a.CompanyId);

            entity.HasOne(a => a.Establishment)
                .WithMany()
                .HasForeignKey(a => a.EstablishmentId);

            entity.HasOne(a => a.EmissionPoint)
                .WithMany()
                .HasForeignKey(a => a.EmissionPointId);

            entity.HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId);
        });

        modelBuilder.Entity<Sale>(entity =>
        {
            entity.Property(s => s.Status)
                .HasConversion<int>()
                .HasDefaultValue(SaleStatus.Completed);

            entity.Property(s => s.PaymentMethod)
                .HasConversion<int>();

            entity.Property(s => s.DocumentType)
                .HasConversion<int>();

            entity.Property(s => s.DocumentStatus)
                .HasConversion<int>()
                .HasDefaultValue(SaleDocumentStatus.NotRequired);

            entity.Property(s => s.GrossSubtotal)
                .HasPrecision(18, 2);

            entity.Property(s => s.DiscountAmount)
                .HasPrecision(18, 2);

            entity.Property(s => s.Subtotal)
                .HasPrecision(18, 2);

            entity.Property(s => s.TaxAmount)
                .HasPrecision(18, 2);

            entity.Property(s => s.Vat15Subtotal)
                .HasPrecision(18, 2);

            entity.Property(s => s.Vat5Subtotal)
                .HasPrecision(18, 2);

            entity.Property(s => s.Vat0Subtotal)
                .HasPrecision(18, 2);

            entity.Property(s => s.VatExemptSubtotal)
                .HasPrecision(18, 2);

            entity.Property(s => s.VatNotSubjectSubtotal)
                .HasPrecision(18, 2);

            entity.Property(s => s.Total)
                .HasPrecision(18, 2);

            entity.Property(s => s.Number)
                .HasMaxLength(50);

            entity.Property(s => s.EstablishmentCodeSnapshot)
                .HasMaxLength(3);

            entity.Property(s => s.EmissionPointCodeSnapshot)
                .HasMaxLength(3);

            entity.Property(s => s.AccessKey)
                .HasMaxLength(49);

            entity.Property(s => s.AuthorizationNumber)
                .HasMaxLength(50);

            entity.Property(s => s.SriNumericCode)
                .HasMaxLength(8);

            entity.Property(s => s.SriXmlDraft)
                .HasColumnType("text");

            entity.Property(s => s.SriSignedXml)
                .HasColumnType("text");

            entity.Property(s => s.SriSignatureHash)
                .HasMaxLength(64);

            entity.Property(s => s.SriSigningCertificateThumbprint)
                .HasMaxLength(100);

            entity.Property(s => s.SriSigningCertificateSubject)
                .HasMaxLength(500);

            entity.Property(s => s.SriSigningCertificateSerialNumber)
                .HasMaxLength(100);

            entity.Property(s => s.SriReceptionStatus)
                .HasMaxLength(50);

            entity.Property(s => s.SriAuthorizationStatus)
                .HasMaxLength(50);

            entity.Property(s => s.SriLastSubmissionError)
                .HasMaxLength(1000);

            entity.Property(s => s.Notes)
                .HasMaxLength(500);

            entity.HasIndex(s => s.CompanyId);
            entity.HasIndex(s => s.EstablishmentId);
            entity.HasIndex(s => s.EmissionPointId);
            entity.HasIndex(s => s.CustomerId);
            entity.HasIndex(s => s.CreatedAt);
            entity.HasIndex(s => s.Status);
            entity.HasIndex(s => s.Number);
            entity.HasIndex(s => s.DocumentStatus);
            entity.HasIndex(s => new { s.CompanyId, s.EstablishmentId, s.EmissionPointId, s.DocumentType, s.Sequential })
                .IsUnique()
                .HasFilter(@"""Sequential"" IS NOT NULL");
            entity.HasIndex(s => new { s.CompanyId, s.EstablishmentId, s.EmissionPointId, s.CreatedAt });

            entity.HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId);

            entity.HasOne(s => s.Customer)
                .WithMany()
                .HasForeignKey(s => s.CustomerId);

            entity.HasOne(s => s.Company)
                .WithMany()
                .HasForeignKey(s => s.CompanyId);

            entity.HasOne(s => s.Establishment)
                .WithMany()
                .HasForeignKey(s => s.EstablishmentId);

            entity.HasOne(s => s.EmissionPoint)
                .WithMany()
                .HasForeignKey(s => s.EmissionPointId);
        });

        modelBuilder.Entity<SriSubmissionAttempt>(entity =>
        {
            entity.Property(a => a.AccessKey)
                .IsRequired()
                .HasMaxLength(49);

            entity.Property(a => a.AttemptType)
                .HasConversion<int>();

            entity.Property(a => a.Status)
                .HasConversion<int>();

            entity.Property(a => a.ReceptionStatus)
                .HasMaxLength(50);

            entity.Property(a => a.AuthorizationStatus)
                .HasMaxLength(50);

            entity.Property(a => a.AuthorizationNumber)
                .HasMaxLength(50);

            entity.Property(a => a.RequestXmlSnapshot)
                .HasColumnType("text");

            entity.Property(a => a.ResponseXml)
                .HasColumnType("text");

            entity.Property(a => a.ErrorCode)
                .HasMaxLength(100);

            entity.Property(a => a.ErrorMessage)
                .HasMaxLength(1000);

            entity.Property(a => a.SriMessageIdentifier)
                .HasMaxLength(100);

            entity.Property(a => a.SriMessageType)
                .HasMaxLength(100);

            entity.Property(a => a.SriMessage)
                .HasMaxLength(1000);

            entity.Property(a => a.SriAdditionalInfo)
                .HasMaxLength(2000);

            entity.HasIndex(a => new { a.SaleId, a.CreatedAt });
            entity.HasIndex(a => new { a.CompanyId, a.AccessKey });
            entity.HasIndex(a => new { a.CompanyId, a.CreatedAt });
            entity.HasIndex(a => a.AccessKey);

            entity.HasOne(a => a.Sale)
                .WithMany(s => s.SriSubmissionAttempts)
                .HasForeignKey(a => a.SaleId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(a => a.Company)
                .WithMany()
                .HasForeignKey(a => a.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(a => a.Establishment)
                .WithMany()
                .HasForeignKey(a => a.EstablishmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(a => a.EmissionPoint)
                .WithMany()
                .HasForeignKey(a => a.EmissionPointId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(a => a.CreatedByUser)
                .WithMany()
                .HasForeignKey(a => a.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SaleInvoiceEmailDelivery>(entity =>
        {
            entity.Property(d => d.ToEmail)
                .IsRequired()
                .HasMaxLength(320);

            entity.Property(d => d.CcEmail)
                .HasMaxLength(320);

            entity.Property(d => d.Subject)
                .IsRequired()
                .HasMaxLength(180);

            entity.Property(d => d.Status)
                .IsRequired()
                .HasMaxLength(30);

            entity.Property(d => d.DocumentNumberSnapshot)
                .HasMaxLength(50);

            entity.Property(d => d.AuthorizationNumberSnapshot)
                .HasMaxLength(50);

            entity.Property(d => d.ErrorCode)
                .HasMaxLength(100);

            entity.Property(d => d.ErrorMessage)
                .HasMaxLength(500);

            entity.HasIndex(d => new { d.SaleId, d.CreatedAt });
            entity.HasIndex(d => new { d.CompanyId, d.CreatedAt });

            entity.HasOne(d => d.Sale)
                .WithMany(s => s.InvoiceEmailDeliveries)
                .HasForeignKey(d => d.SaleId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.Company)
                .WithMany()
                .HasForeignKey(d => d.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.Establishment)
                .WithMany()
                .HasForeignKey(d => d.EstablishmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.EmissionPoint)
                .WithMany()
                .HasForeignKey(d => d.EmissionPointId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.CreatedByUser)
                .WithMany()
                .HasForeignKey(d => d.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SaleItem>(entity =>
        {
            entity.Property(si => si.Quantity)
                .HasPrecision(18, 4);

            entity.Property(si => si.UnitPrice)
                .HasPrecision(18, 2);

            entity.Property(si => si.GrossSubtotal)
                .HasPrecision(18, 2);

            entity.Property(si => si.DiscountAmount)
                .HasPrecision(18, 2);

            entity.Property(si => si.NetSubtotal)
                .HasPrecision(18, 2);

            entity.Property(si => si.LineSubtotal)
                .HasPrecision(18, 2);

            entity.Property(si => si.VatCategory)
                .HasConversion<int>()
                .HasDefaultValue(ProductVatCategory.Vat15);

            entity.Property(si => si.VatRate)
                .HasPrecision(9, 4);

            entity.Property(si => si.TaxableSubtotal)
                .HasPrecision(18, 2);

            entity.Property(si => si.TaxAmount)
                .HasPrecision(18, 2);

            entity.Property(si => si.LineTotal)
                .HasPrecision(18, 2);

            entity.HasOne(si => si.Sale)
                .WithMany(s => s.Items)
                .HasForeignKey(si => si.SaleId);

            entity.HasOne(si => si.Product)
                .WithMany()
                .HasForeignKey(si => si.ProductId);

            entity.HasIndex(si => si.SaleId);
            entity.HasIndex(si => si.ProductId);
        });
    }
}
