using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NiakuroTcg.Web;

public static class PlaytestNoteEndpoint
{
    private const int MaxRecordLength = 16_000;
    private const int InlineRecordLength = 1_800;

    public static IEndpointRouteBuilder MapPlaytestNoteEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/playtest-note", SendAsync)
            .RequireRateLimiting("playtest-notes");
        return endpoints;
    }

    private static async Task<IResult> SendAsync(
        PlaytestNoteRequest request,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<PlaytestNoteEndpoint> logger,
        CancellationToken cancellationToken)
    {
        var webhookUrl = configuration["Discord:WebhookUrl"];
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return Results.Problem(
                "Discord送信がサーバーに設定されていません。",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var webhookUri)
            || webhookUri.Scheme != Uri.UriSchemeHttps
            || webhookUri.Port != 443
            || (webhookUri.Host != "discord.com" && webhookUri.Host != "discordapp.com")
            || !webhookUri.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal))
        {
            logger.LogError("Discord webhook URL configuration is invalid.");
            return Results.Problem(
                "Discord送信先のサーバー設定が正しくありません。",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        if (string.IsNullOrWhiteSpace(request.Record))
            return Results.BadRequest(new { message = "送信する試遊記録がありません。" });

        if (request.Record.Length > MaxRecordLength)
            return Results.BadRequest(new { message = "試遊記録が長すぎます。ダウンロードして保存してください。" });

        var client = httpClientFactory.CreateClient();
        HttpContent content;
        if (request.Record.Length <= InlineRecordLength)
        {
            var payload = JsonSerializer.Serialize(new
            {
                content = $"試遊記録\n{request.Record}",
                allowed_mentions = new { parse = Array.Empty<string>() }
            });
            content = new StringContent(payload, Encoding.UTF8, "application/json");
        }
        else
        {
            var form = new MultipartFormDataContent();
            var payload = JsonSerializer.Serialize(new
            {
                content = "長い試遊記録をテキストファイルで添付しました。",
                allowed_mentions = new { parse = Array.Empty<string>() }
            });
            form.Add(new StringContent(payload, Encoding.UTF8, "application/json"), "payload_json");
            var file = new ByteArrayContent(Encoding.UTF8.GetBytes(request.Record));
            file.Headers.ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" };
            form.Add(file, "files[0]", "shiyu-record.txt");
            content = form;
        }

        using (content)
        {
            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(webhookUri, content, cancellationToken);
            }
            catch (HttpRequestException)
            {
                logger.LogError("Discord webhook request failed.");
                return Results.Problem(
                    "Discordへの接続に失敗しました。サーバー設定やネットワークを確認してください。",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Discord webhook returned HTTP {StatusCode}.", (int)response.StatusCode);
                    return Results.Problem(
                        "Discordへの送信に失敗しました。サーバー設定を確認してください。",
                        statusCode: StatusCodes.Status502BadGateway);
                }
            }
        }

        return Results.Ok(new { message = "Discordに試遊記録を送信しました。" });
    }
}

public sealed record PlaytestNoteRequest(string? Record);
