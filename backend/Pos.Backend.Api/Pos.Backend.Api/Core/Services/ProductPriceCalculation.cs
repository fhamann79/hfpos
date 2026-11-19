using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Core.Services;

public static class ProductPriceCalculation
{
    // Batch transport/UI bound: USD cents and four-decimal costs remain faithfully displayable in JS.
    // This deliberately does not change Product's database or legacy-editor limits.
    public const decimal MaxPrice = 1000000000m;

    public static void Validate(string action, decimal value)
    {
        if (action is not ("increase-percent" or "decrease-percent" or "increase-amount"
            or "decrease-amount" or "set-price" or "gross-margin") || value < 0
            || value > MaxPrice || decimal.Round(value, 4) != value || (action == "decrease-percent" && value > 100)
            || (action == "gross-margin" && value >= 100))
            throw new OperationalContextException("PRICE_BATCH_ACTION_INVALID", 400);
    }

    public static (decimal? Price, string? Error, string[] Warnings) Evaluate(
        decimal price, decimal cost, string action, decimal value)
    {
        Validate(action, value);
        if (price < 0 || cost < 0) return (null, "PRODUCT_AMOUNT_INVALID", []);
        if (!TransportSafe(price, cost)) return (null, "PRICE_BATCH_AMOUNT_LIMIT", []);
        if (action == "gross-margin" && cost == 0) return (null, "GROSS_MARGIN_ZERO_COST", ["ZERO_COST"]);
        decimal proposed;
        try
        {
            proposed = action switch
            {
                "increase-percent" => price * (1 + value / 100),
                "decrease-percent" => price * (1 - value / 100),
                "increase-amount" => price + value,
                "decrease-amount" => price - value,
                "set-price" => value,
                "gross-margin" => cost / (1 - value / 100),
                _ => throw new InvalidOperationException()
            };
            proposed = decimal.Round(proposed, 2, MidpointRounding.AwayFromZero);
        }
        catch (OverflowException) { return (null, "PRICE_OUT_OF_RANGE", []); }
        if (proposed < 0 || proposed > MaxPrice) return (null, "PRICE_OUT_OF_RANGE", []);
        if (action == "gross-margin" && proposed <= 0) return (null, "GROSS_MARGIN_ROUNDS_TO_ZERO", []);
        var warnings = new List<string>();
        if (cost == 0) warnings.Add("ZERO_COST"); // Cost is non-nullable; zero is not unknown.
        if (proposed == 0) warnings.Add("ZERO_PRICE");
        if (proposed < cost) warnings.Add("BELOW_COST");
        if (price == 0 ? proposed > 0 : proposed >= price * 2 || proposed <= price / 2)
            warnings.Add("EXTREME_CHANGE");
        return (proposed, null, warnings.ToArray());
    }

    public static bool TransportSafe(decimal price, decimal cost) => price >= 0 && price <= MaxPrice
        && cost >= 0 && cost <= MaxPrice && decimal.Round(price, 2) == price && decimal.Round(cost, 4) == cost;

    public static decimal? Margin(decimal price, decimal cost) => price > 0
        ? decimal.Round((1 - cost / price) * 100, 4, MidpointRounding.AwayFromZero) : null;
}
