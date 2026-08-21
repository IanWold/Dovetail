namespace Dovetail.Example.Business;

// Wrappers around int IDs to make the types distinct for the Dovetail pipelines

public readonly record struct Sku(int Value);
public readonly record struct UserId(int Value);
public readonly record struct CartId(int Value);
public readonly record struct OrderId(int Value);
