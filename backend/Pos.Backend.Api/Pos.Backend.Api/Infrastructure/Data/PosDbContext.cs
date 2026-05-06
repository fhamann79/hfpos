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
    public DbSet<Establishment> Establishments { get; set; }
    public DbSet<EmissionPoint> EmissionPoints { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<Customer> Customers { get; set; }
    public DbSet<ProductStock> ProductStocks { get; set; }
    public DbSet<InventoryMovement> InventoryMovements { get; set; }
    public DbSet<Sale> Sales { get; set; }
    public DbSet<SaleItem> SaleItems { get; set; }

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

        modelBuilder.Entity<Sale>(entity =>
        {
            entity.Property(s => s.Status)
                .HasConversion<int>()
                .HasDefaultValue(SaleStatus.Completed);

            entity.Property(s => s.PaymentMethod)
                .HasConversion<int>();

            entity.Property(s => s.DocumentType)
                .HasConversion<int>();

            entity.Property(s => s.Subtotal)
                .HasPrecision(18, 2);

            entity.Property(s => s.Total)
                .HasPrecision(18, 2);

            entity.Property(s => s.Number)
                .HasMaxLength(50);

            entity.Property(s => s.Notes)
                .HasMaxLength(500);

            entity.HasIndex(s => s.CompanyId);
            entity.HasIndex(s => s.EstablishmentId);
            entity.HasIndex(s => s.EmissionPointId);
            entity.HasIndex(s => s.CustomerId);
            entity.HasIndex(s => s.CreatedAt);
            entity.HasIndex(s => s.Status);
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

        modelBuilder.Entity<SaleItem>(entity =>
        {
            entity.Property(si => si.Quantity)
                .HasPrecision(18, 4);

            entity.Property(si => si.UnitPrice)
                .HasPrecision(18, 2);

            entity.Property(si => si.LineSubtotal)
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
