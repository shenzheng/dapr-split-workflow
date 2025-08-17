// OrderProcessorMainWorkflow/Program.cs
using Dapr.Client;
using Dapr.Workflow;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprClient();
builder.Services.AddDaprWorkflow(options =>
{
    options.RegisterWorkflow<OrderWorkflow>();

    // 注册 Activity（薄封装，内部远程调用对端服务）
    options.RegisterActivity<VerifyInventoryActivity>();
    options.RegisterActivity<RequestApprovalActivity>();
    options.RegisterActivity<ProcessPaymentActivity>();
    options.RegisterActivity<UpdateInventoryActivity>();
    options.RegisterActivity<NotifyActivity>();
});

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok("ok"));

// 启动一个订单流程
app.MapPost("/start", async (
    [FromServices] DaprWorkflowClient wf,
    [FromBody] OrderInput input) =>
{
    var instanceId = input.OrderId ?? Guid.NewGuid().ToString("n")[..8];
    await wf.ScheduleNewWorkflowAsync(
        name: nameof(OrderWorkflow),
        input: input,
        instanceId: instanceId);

    Console.WriteLine($"[Start] 订单 {instanceId} 已启动");

    return Results.Accepted($"/instances/{instanceId}", new { instanceId });
});

// 查看状态
app.MapGet("/instances/{id}", async (
    [FromServices] DaprWorkflowClient wf, string id) =>
{
    var s = await wf.GetWorkflowStateAsync(id);
    return s is null ? Results.NotFound() : Results.Ok(s);
});

app.Run();

// ========= 工作流 =========
public sealed class OrderWorkflow : Workflow<OrderInput, string>
{
    public override async Task<string> RunAsync(WorkflowContext ctx, OrderInput input)
    {
        Console.WriteLine($"[OrderWorkflow] 处理订单 {input.OrderId} for {input.ItemId} x {input.Qty} @ {input.Amount}");

        var inv = await ctx.CallActivityAsync<VerifyInventoryResult>(nameof(VerifyInventoryActivity),
            new VerifyInventory(input.OrderId!, input.ItemId!, input.Qty));

        if (!inv.Ok)
        {
            await ctx.CallActivityAsync<object?>(nameof(NotifyActivity),
                new Notification($"Order {input.OrderId} failed: inventory not available."));
            return "Rejected(NoInventory)";
        }

        var approval = await ctx.CallActivityAsync<ApprovalResult>(nameof(RequestApprovalActivity),
            new Approval(input.OrderId!, input.Amount));

        if (!approval.Approved)
        {
            await ctx.CallActivityAsync<object?>(nameof(NotifyActivity),
                new Notification($"Order {input.OrderId} rejected: not approved."));
            return "Rejected(NotApproved)";
        }

        var pay = await ctx.CallActivityAsync<PaymentResult>(nameof(ProcessPaymentActivity),
            new Payment(input.OrderId!, input.Amount));

        if (!pay.Paid)
        {
            await ctx.CallActivityAsync<object?>(nameof(NotifyActivity),
                new Notification($"Order {input.OrderId} failed: payment error."));
            return "Rejected(PaymentFailed)";
        }

        await ctx.CallActivityAsync<UpdateInventoryResult>(nameof(UpdateInventoryActivity),
            new UpdateInventory(input.OrderId!, input.ItemId!, input.Qty));

        await ctx.CallActivityAsync<object?>(nameof(NotifyActivity),
            new Notification($"Order {input.OrderId} completed."));
        return "Completed";
    }
}

// ========= Activity 薄封装（远程调用对端服务）=========
public abstract class RemoteActivityBase<TInput, TOutput> : WorkflowActivity<TInput, TOutput>
    where TInput : class
    where TOutput : class
{
    protected const string ActivitiesAppId = "order-activities";
    protected readonly DaprClient Dapr;

    protected RemoteActivityBase(DaprClient dapr) => Dapr = dapr;

    protected async Task<TOut?> CallAsync<TIn, TOut>(string method, TIn input) =>
        await Dapr.InvokeMethodAsync<TIn, TOut>(ActivitiesAppId, method, input);

    protected Task CallAsync<TIn>(string method, TIn input) =>
        Dapr.InvokeMethodAsync(ActivitiesAppId, method, input);
}

public sealed class VerifyInventoryActivity(DaprClient d)
    : RemoteActivityBase<VerifyInventory, VerifyInventoryResult>(d)
{
    public override Task<VerifyInventoryResult> RunAsync(WorkflowActivityContext _,
        VerifyInventory input) => CallAsync<VerifyInventory, VerifyInventoryResult>("verify-inventory", input)!;
}

public sealed class RequestApprovalActivity(DaprClient d)
    : RemoteActivityBase<Approval, ApprovalResult>(d)
{
    public override Task<ApprovalResult> RunAsync(WorkflowActivityContext _,
        Approval input) => CallAsync<Approval, ApprovalResult>("request-approval", input)!;
}

public sealed class ProcessPaymentActivity(DaprClient d)
    : RemoteActivityBase<Payment, PaymentResult>(d)
{
    public override Task<PaymentResult> RunAsync(WorkflowActivityContext _,
        Payment input) => CallAsync<Payment, PaymentResult>("process-payment", input)!;
}

public sealed class UpdateInventoryActivity(DaprClient d)
    : RemoteActivityBase<UpdateInventory, UpdateInventoryResult>(d)
{
    public override Task<UpdateInventoryResult> RunAsync(WorkflowActivityContext _,
        UpdateInventory input) => CallAsync<UpdateInventory, UpdateInventoryResult>("update-inventory", input)!;
}

public sealed class NotifyActivity(DaprClient d)
    : RemoteActivityBase<Notification, object>(d)
{
    public override async Task<object> RunAsync(WorkflowActivityContext _, Notification input)
    {
        // 不要用 <TIn, TOut> 的重载
        await CallAsync("notify", input);

        return null!;
    }
}

// ========= 共享模型 =========
public record OrderInput(string? OrderId, string ItemId, int Qty, decimal Amount);
public record VerifyInventory(string OrderId, string ItemId, int Qty);
public record VerifyInventoryResult(bool Ok, string? Message);
public record Approval(string OrderId, decimal Amount);
public record ApprovalResult(bool Approved, string? Note);
public record Payment(string OrderId, decimal Amount);
public record PaymentResult(bool Paid, string? TxnId);
public record UpdateInventory(string OrderId, string ItemId, int Qty);
public record UpdateInventoryResult(bool Ok);
public record Notification(string Message);