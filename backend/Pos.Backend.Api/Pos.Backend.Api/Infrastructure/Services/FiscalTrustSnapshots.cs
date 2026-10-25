using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

internal static class FiscalTrustSnapshots
{
    // PostgreSQL computes a public change token without returning ciphertext to the API service.
    internal static IQueryable<FiscalTenantTrustSnapshot> Query(PosDbContext db) => db.Database.SqlQuery<FiscalTenantTrustSnapshot>($"""
        SELECT c."Id" AS "CompanyId", c."Ruc", c."IsActive" AS "CompanyActive",
          cert."Id" AS "CertificateId", cert."Thumbprint", cert."NotAfter", cert."UploadedAt",
          cert."NotBefore", cert."HasPrivateKey", cert."SerialNumber",
          encode(sha256(int4send(octet_length(cert."EncryptedCertificateBytes")) || cert."EncryptedCertificateBytes"
            || int4send(octet_length(cert."EncryptedPassword")) || cert."EncryptedPassword"), 'hex') AS "RowDigest",
          s."Environment", s."IsEnabled" AS "SriEnabled", s."UpdatedAt" AS "SettingsUpdatedAt"
        FROM "Companies" c
        LEFT JOIN "CompanySriCertificates" cert ON cert."CompanyId" = c."Id" AND cert."IsActive"
        LEFT JOIN "CompanySriSettings" s ON s."CompanyId" = c."Id"
        """);
}
