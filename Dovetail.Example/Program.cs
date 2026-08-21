using System.Dynamic;
using Dovetail.Example.Infrastructure;
using Dovetail.Example.Presentation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ProductCatalogDataAccess>();
builder.Services.AddSingleton<InventoryDataAccess>();
builder.Services.AddSingleton<ReviewDataAccess>();
builder.Services.AddSingleton<CartDataAccess>();
builder.Services.AddSingleton<CustomerAccountDataAccess>();
builder.Services.AddSingleton<LoyaltyDataAccess>();
builder.Services.AddSingleton<OrderDataAccess>();
builder.Services.AddSingleton<PaymentDataAccess>();
builder.Services.AddSingleton<ShipmentTrackingDataAccess>();

builder.Services.AddPipelines();

var app = builder.Build();

Tracing.Enable();

app.MapIndexEndpoints();
app.MapProductEndpoints();
app.MapCartEndpoints();
app.MapOrderEndpoints();

app.Run();
