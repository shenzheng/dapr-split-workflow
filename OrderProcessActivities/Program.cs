// OrderProcessActivities/Program.cs
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddDapr();   // 可选：方便以后用 Dapr 特性
var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok("ok"));

app.MapPost("/verify-inventory", ([FromBody] VerifyInventory req) =>
{
    Console.WriteLine($"查询库存-[verify-inventory] Qty {req.Qty} reserved for {req.ItemId}");
    return Results.Ok(new VerifyInventoryResult(true, $"Qty {req.Qty} reserved for {req.ItemId}"));
});

app.MapPost("/request-approval", ([FromBody] Approval req) =>
{
    Console.WriteLine($"审批逻辑/人工回调-[request-approval] auto-approved for {req.OrderId}");
    return Results.Ok(new ApprovalResult(true, "auto-approved"));
});

app.MapPost("/process-payment", ([FromBody] Payment req) =>
{
    Console.WriteLine($"调用支付网关-[process-payment] {req.Amount} for {req.OrderId}");
    return Results.Ok(new PaymentResult(true, $"charged {req.Amount} for {req.OrderId}"));
});

app.MapPost("/update-inventory", ([FromBody] UpdateInventory req) =>
{
    Console.WriteLine($"扣减库存-[update-inventory] {req.Qty} for {req.ItemId} in order {req.OrderId}");
    return Results.Ok(new UpdateInventoryResult(true));
});

app.MapPost("/notify", ([FromBody] Notification req) =>
{
    Console.WriteLine($"通知-[Notify] {req.Message}");
    return Results.Ok();
});

app.Run();

// 请求/响应模型（简单起见放一处）
record VerifyInventory(string OrderId, string ItemId, int Qty);
record VerifyInventoryResult(bool Ok, string? Message);
record Approval(string OrderId, decimal Amount);
record ApprovalResult(bool Approved, string? Note);
record Payment(string OrderId, decimal Amount);
record PaymentResult(bool Paid, string? TxnId);
record UpdateInventory(string OrderId, string ItemId, int Qty);
record UpdateInventoryResult(bool Ok);
record Notification(string Message);