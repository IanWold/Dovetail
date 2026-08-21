namespace Dovetail.Example.Infrastructure;

// This layer only fetches records — no discount rules, no stock-level thresholds.
// Those are business rules and live in Business/Pipelines, operating on the domain
// types Business maps these raw records into.

internal record ProductRecord(int Sku, string Name, string Description, string Category, decimal BasePrice);
