using System.Net.Http.Json;
using System.Text.Json;

namespace PZServerDiscordBot;

public sealed record WorkshopItem(string Id, string Title, DateTimeOffset Updated)
{
    public string Url => $"https://steamcommunity.com/sharedfiles/filedetails/?id={Id}";
}

public static class SteamWorkshop
{
    private static readonly HttpClient http = new();

    public static async Task<IReadOnlyList<WorkshopItem>> GetDetailsAsync(IReadOnlyList<string> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];

        var form = new Dictionary<string, string> { ["itemcount"] = ids.Count.ToString() };
        for (var i = 0; i < ids.Count; i++) form[$"publishedfileids[{i}]"] = ids[i];

        using var response = await http.PostAsync(
            "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
            new FormUrlEncodedContent(form), ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var items = new List<WorkshopItem>();

        foreach (var item in doc.RootElement.GetProperty("response").GetProperty("publishedfiledetails").EnumerateArray())
        {
            if (!item.TryGetProperty("time_updated", out var updated)) continue;
            items.Add(new WorkshopItem(
                item.GetProperty("publishedfileid").GetString() ?? "",
                item.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                DateTimeOffset.FromUnixTimeSeconds(updated.GetInt64())));
        }

        return items;
    }
}
