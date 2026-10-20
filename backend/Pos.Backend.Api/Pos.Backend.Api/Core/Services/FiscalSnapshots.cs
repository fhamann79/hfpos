using System.Text.Json;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Core.Services;

public static class FiscalSnapshots
{
    public static FiscalIssuerSnapshot? Read(string? json) => json is null ? null
        : JsonSerializer.Deserialize<FiscalIssuerSnapshot>(json)
            ?? throw new InvalidOperationException("FISCAL_SNAPSHOT_INVALID");

    public static Company IssuerCompany(FiscalIssuerSnapshot snapshot) => new()
    {
        Name = snapshot.Name, Ruc = snapshot.Ruc, TradeName = snapshot.TradeName,
        MatrixAddress = snapshot.MatrixAddress, IsAccountingRequired = snapshot.IsAccountingRequired,
        SpecialTaxpayerNumber = snapshot.SpecialTaxpayerResolution,
        TaxpayerRegime = snapshot.Regime switch
        {
            FiscalRegime.PopularBusiness => "RIMPE NEGOCIO POPULAR",
            FiscalRegime.Entrepreneur => "RIMPE EMPRENDEDOR",
            _ => "GENERAL"
        }
    };
}
