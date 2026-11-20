using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using NiakuroTcg.Web;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddSignalR();
builder.Services.AddHttpClient();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("playtest-notes", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { detail = "送信回数が上限に達しました。1分後に再度お試しください。" },
            cancellationToken);
    };
});
builder.Services.AddSingleton<NiakuroTcg.Web.RoomManager>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.MapHub<NiakuroTcg.Web.GameHub>("/gameHub");
app.MapPlaytestNoteEndpoint();

app.Run();
