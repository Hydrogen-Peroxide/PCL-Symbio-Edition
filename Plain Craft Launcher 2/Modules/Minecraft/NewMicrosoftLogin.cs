using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using PCL.Core.App;

namespace PCL;

/// <summary>独立的微软登录实现，供新的登录入口使用。</summary>
internal static class NewMicrosoftLogin
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task LoginAsync(CancellationToken cancellationToken = default)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = Secrets.MSOAuthClientId,
            ["scope"] = "XboxLive.signin offline_access"
        });
        var device = await PostJson("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode", form, cancellationToken);
        var code = device["device_code"]?.ToString() ?? throw new InvalidOperationException("Microsoft did not return a device code.");
        ModBase.ClipboardSet(device["user_code"]?.ToString() ?? "");
        ModBase.OpenWebsite(device["verification_uri"]?.ToString() ?? "https://microsoft.com/devicelogin");

        var interval = Math.Max(5, device["interval"]?.GetValue<int>() ?? 5);
        JsonObject token;
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);
            using var tokenForm = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = Secrets.MSOAuthClientId,
                ["device_code"] = code,
                ["scope"] = "XboxLive.signin offline_access"
            });
            var response = await Http.PostAsync("https://login.microsoftonline.com/consumers/oauth2/v2.0/token", tokenForm, cancellationToken);
            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?.AsObject() ?? new();
            if (body["error"]?.ToString() == "authorization_pending") continue;
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(body["error_description"]?.ToString() ?? "Microsoft login failed.");
            token = body;
            break;
        }

        var access = token["access_token"]?.ToString() ?? throw new InvalidOperationException("OAuth token is missing.");
        using var xblContent = JsonContent(new { Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = $"d={access}" }, RelyingParty = "http://auth.xboxlive.com", TokenType = "JWT" });
        var xbl = await PostJson("https://user.auth.xboxlive.com/user/authenticate", xblContent, cancellationToken);
        var xblToken = xbl["Token"]?.ToString() ?? throw new InvalidOperationException("Xbox Live authentication failed.");
        var userHash = xbl["DisplayClaims"]?["xui"]?[0]?["uhs"]?.ToString() ?? throw new InvalidOperationException("Xbox user hash is missing.");
        using var xstsContent = JsonContent(new { Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xblToken } }, RelyingParty = "rp://api.minecraftservices.com/", TokenType = "JWT" });
        var xsts = await PostJson("https://xsts.auth.xboxlive.com/xsts/authorize", xstsContent, cancellationToken);
        var xstsToken = xsts["Token"]?.ToString() ?? throw new InvalidOperationException("Xbox security token is missing.");
        using var mcContent = JsonContent(new { identityToken = $"XBL3.0 x={userHash};{xstsToken}" });
        var mc = await PostJson("https://api.minecraftservices.com/authentication/login_with_xbox", mcContent, cancellationToken);
        var mcToken = mc["access_token"]?.ToString() ?? throw new InvalidOperationException("Minecraft token is missing.");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcToken);
        var profile = JsonNode.Parse(await (await Http.SendAsync(request, cancellationToken)).Content.ReadAsStringAsync(cancellationToken))?.AsObject() ?? new();
        var uuid = profile["id"]?.ToString() ?? throw new InvalidOperationException("Minecraft profile is missing.");
        var name = profile["name"]?.ToString() ?? "Minecraft Player";
        var newProfile = new ModProfile.McProfile { Type = ModLaunch.McLoginType.Ms, Uuid = uuid, Username = name, Name = name, AccessToken = mcToken, RefreshToken = token["refresh_token"]?.ToString() ?? "", ClientToken = uuid, Expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds() };
        ModProfile.profileList.Add(newProfile);
        ModProfile.selectedProfile = newProfile;
        ModProfile.SaveProfile();
    }

    private static StringContent JsonContent(object value) => new(System.Text.Json.JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json");
    private static async Task<JsonObject> PostJson(string url, HttpContent content, CancellationToken cancellationToken) { using (content) { var response = await Http.PostAsync(url, content, cancellationToken); var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?.AsObject() ?? new(); if (!response.IsSuccessStatusCode) throw new InvalidOperationException(node["errorMessage"]?.ToString() ?? node["error_description"]?.ToString() ?? $"Request failed: {(int)response.StatusCode}"); return node; } }
}
