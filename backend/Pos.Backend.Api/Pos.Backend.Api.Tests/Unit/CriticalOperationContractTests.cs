using Microsoft.Extensions.Logging.Abstractions;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.WebApi.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace Pos.Backend.Api.Tests.Unit;

public sealed class CriticalOperationContractTests
{
    [Fact]
    public async Task Purchase_requires_nonempty_id_and_rejects_quantized_zero_before_context_or_storage()
    {
        var service = new PurchaseReceiptService(null!, null!, null!, null!, null!, null!);
        var request = new PurchaseReceiptCreateDto { SupplierId = 1, Items = [new() { ProductId = 1, Quantity = 1m }] };
        Assert.Equal("REQUEST_ID_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(request))).Message);
        request.RequestId = Guid.NewGuid(); request.Items[0].Quantity = 0.00001m;
        Assert.Equal("PURCHASE_RECEIPT_QUANTITY_INVALID", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(request))).Message);
    }

    [Theory]
    [InlineData("entry")] [InlineData("exit")] [InlineData("adjust")]
    public void Manual_inventory_requires_an_explicit_id(string kind)
    {
        var service = new InventoryService(null!, NullLogger<InventoryService>.Instance, null!, null!, null!);
        var error = Assert.Throws<InvalidOperationException>(() => {
            _ = kind switch { "entry" => service.RegisterEntryAsync(new() { ProductId = 1, Quantity = 1m }),
                "exit" => service.RegisterExitAsync(new() { ProductId = 1, Quantity = 1m }),
                _ => service.RegisterAdjustmentAsync(new() { ProductId = 1, Quantity = 1m }) };
        });
        Assert.Equal("REQUEST_ID_REQUIRED", error.Message);
    }

    [Fact]
    public async Task Manual_cash_requires_id_and_rejects_money_that_rounds_to_zero()
    {
        var service = new CashSessionService(null!, NullLogger<CashSessionService>.Instance, null!, null!, null!);
        var request = new CreateCashMovementDto { Type = Core.Enums.CashMovementType.CashIn, Amount = 1m, Reason = "Synthetic" };
        Assert.Equal("REQUEST_ID_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddMovementAsync(1, request))).Message);
        request.RequestId = Guid.NewGuid(); request.Amount = 0.001m;
        Assert.Equal("CASH_MOVEMENT_AMOUNT_INVALID", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddMovementAsync(1, request))).Message);
    }

    [Theory]
    [InlineData(typeof(PurchaseReceiptsController), "Create", AppPermissions.PurchasesWrite)]
    [InlineData(typeof(InventoryController), "RegisterEntry", AppPermissions.InventoryWrite)]
    [InlineData(typeof(InventoryController), "RegisterExit", AppPermissions.InventoryWrite)]
    [InlineData(typeof(InventoryController), "RegisterAdjustment", AppPermissions.InventoryWrite)]
    [InlineData(typeof(CashSessionsController), "AddMovement", AppPermissions.CashSessionsWrite)]
    [InlineData(typeof(CashSessionsController), "Open", AppPermissions.CashSessionsWrite)]
    public void Every_mutation_including_replay_keeps_server_side_permission(Type controller, string action, string permission)
    {
        var attributes = controller.GetMethod(action)!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>();
        Assert.Contains(attributes, a => a.Policy == permission);
    }

    [Fact]
    public async Task Optional_opening_key_must_not_be_empty_and_legacy_negative_amount_still_rejects()
    {
        var service = new CashSessionService(null!, NullLogger<CashSessionService>.Instance, null!, null!, null!);
        Assert.Equal("REQUEST_ID_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.OpenAsync(new() { RequestId = Guid.Empty }))).Message);
        Assert.Equal("CASH_SESSION_OPENING_AMOUNT_INVALID", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.OpenAsync(new() { OpeningAmount = -1m }))).Message);
    }
}
