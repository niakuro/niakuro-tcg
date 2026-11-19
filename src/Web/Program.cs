var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddSignalR();
builder.Services.AddSingleton<NiakuroTcg.Web.RoomManager>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<NiakuroTcg.Web.GameHub>("/gameHub");

app.Run();
