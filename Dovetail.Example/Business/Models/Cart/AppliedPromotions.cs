namespace Dovetail.Example.Business;

public record AppliedPromotions(
    IReadOnlyList<string> PromotionCodes,
    decimal SavingsAmount
);
