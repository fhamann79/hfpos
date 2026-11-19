using Microsoft.AspNetCore.Authorization;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.WebApi.Controllers;

namespace Pos.Backend.Api.Tests.Unit;

public sealed class ProductPriceCalculationTests
{
    [Theory]
    [InlineData("increase-percent", 10, 11)]
    [InlineData("decrease-percent", 10, 9)]
    [InlineData("increase-amount", 2.50, 12.50)]
    [InlineData("decrease-amount", 2.50, 7.50)]
    [InlineData("set-price", 1.005, 1.01)]
    [InlineData("gross-margin", 20, 2.50)]
    [InlineData("gross-margin", 0, 2)]
    [InlineData("decrease-percent", 100, 0)]
    public void Calculates_USD_base_prices(string action, double value, double expected)
    {
        var result = ProductPriceCalculation.Evaluate(10m, 2m, action, (decimal)value);
        Assert.Null(result.Error);
        Assert.Equal((decimal)expected, result.Price);
    }

    [Theory]
    [InlineData("gross-margin", -1)] [InlineData("gross-margin", 100)]
    [InlineData("gross-margin", 101)] [InlineData("decrease-percent", 101)]
    [InlineData("set-price", -1)] [InlineData("markup", 20)]
    [InlineData("cost", 2)] [InlineData("unknown", 0)]
    public void Rejects_invalid_actions_and_margin_boundaries(string action, int value)
        => Assert.Equal("PRICE_BATCH_ACTION_INVALID", Assert.Throws<OperationalContextException>(() =>
            ProductPriceCalculation.Evaluate(10, 2, action, value)).ErrorCode);

    [Fact]
    public void Rounds_halves_away_from_zero_and_handles_integer_prices_range_and_overflow()
    {
        Assert.Equal(10.01m, ProductPriceCalculation.Evaluate(10, 2, "increase-amount", .005m).Price);
        Assert.Equal(10m, ProductPriceCalculation.Evaluate(10, 2, "increase-percent", 0).Price);
        Assert.Equal(ProductPriceCalculation.MaxPrice,
            ProductPriceCalculation.Evaluate(10, 2, "set-price", ProductPriceCalculation.MaxPrice).Price);
        Assert.Equal("PRICE_OUT_OF_RANGE", ProductPriceCalculation.Evaluate(10, 2, "decrease-amount", 11).Error);
        Assert.Equal("PRICE_OUT_OF_RANGE", ProductPriceCalculation.Evaluate(ProductPriceCalculation.MaxPrice,
            2, "increase-percent", ProductPriceCalculation.MaxPrice).Error);
        Assert.Equal("PRICE_BATCH_AMOUNT_LIMIT", ProductPriceCalculation.Evaluate(10, decimal.MaxValue, "gross-margin", 99).Error);
        Assert.Equal("PRICE_OUT_OF_RANGE", ProductPriceCalculation.Evaluate(10, ProductPriceCalculation.MaxPrice, "gross-margin", 99.9999m).Error);
        Assert.Throws<OperationalContextException>(() => ProductPriceCalculation.Validate("increase-percent", .00001m));
        Assert.Throws<OperationalContextException>(() => ProductPriceCalculation.Validate("set-price", 1000000000.0001m));
    }

    [Fact]
    public void Zero_cost_is_known_warned_and_not_called_unknown_or_net_profit()
    {
        var result = ProductPriceCalculation.Evaluate(10, 0, "gross-margin", 25);
        Assert.Null(result.Price);
        Assert.Equal("GROSS_MARGIN_ZERO_COST", result.Error);
        Assert.Contains("ZERO_COST", result.Warnings);
        Assert.Equal("GROSS_MARGIN_ROUNDS_TO_ZERO", ProductPriceCalculation.Evaluate(10, .0001m, "gross-margin", 20).Error);
        Assert.Contains("ZERO_PRICE", ProductPriceCalculation.Evaluate(10, 0, "set-price", 0).Warnings);
        var rounded = ProductPriceCalculation.Evaluate(10, 2, "gross-margin", 33);
        Assert.Equal(2.99m, rounded.Price);
        Assert.Equal(33.1104m, ProductPriceCalculation.Margin(rounded.Price!.Value, 2));
        Assert.Null(ProductPriceCalculation.Margin(0, 0));
        Assert.Equal(20m, ProductPriceCalculation.Margin(2.5m, 2));
        Assert.Contains("BELOW_COST", ProductPriceCalculation.Evaluate(10, 2, "set-price", 1).Warnings);
        Assert.Contains("EXTREME_CHANGE", ProductPriceCalculation.Evaluate(10, 2, "set-price", 100).Warnings);
    }

    [Fact]
    public void New_capability_is_admin_seeded_and_requires_no_fiscal_permission_or_legacy_editor_change()
    {
        var policies = typeof(ProductPriceBatchesController).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Select(a => a.Policy).ToArray();
        Assert.Equal(new[] { AppPermissions.CatalogProductsRead, AppPermissions.CatalogProductPricesBatch }, policies);
        Assert.Contains(TenantDefaults.Permissions, p => p.Code == AppPermissions.CatalogProductPricesBatch);
        Assert.Contains(AppPermissions.CatalogProductPricesBatch, TenantDefaults.RolePermissions[AppRoles.Admin]);
        Assert.DoesNotContain(AppPermissions.CatalogProductPricesBatch, TenantDefaults.RolePermissions[AppRoles.Supervisor]);
        Assert.DoesNotContain(AppPermissions.CatalogProductPricesBatch, TenantDefaults.RolePermissions[AppRoles.Cashier]);
    }
}
