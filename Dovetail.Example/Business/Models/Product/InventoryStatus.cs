namespace Dovetail.Example.Business;

public record InventoryStatus(
    StockLevel Level,
    int? UnitsAvailable
);
