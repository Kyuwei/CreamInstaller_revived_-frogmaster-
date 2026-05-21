using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using CreamInstaller.Platforms.Epic.GraphQL;
using CreamInstaller.Utility;
using Newtonsoft.Json;

#if DEBUG
using CreamInstaller.Forms;
#endif

namespace CreamInstaller.Platforms.Epic;

internal static class EpicStore
{
    private const int Cooldown = 600;

    internal static async Task<List<(string id, string name, string product, string icon, string developer)>>
        QueryCatalog(string categoryNamespace)
    {
        List<(string id, string name, string product, string icon, string developer)> dlcIds = [];
        string cacheFile = ProgramData.AppInfoPath + @$"\{SanitizeCacheKey(categoryNamespace)}.json";
        string cachedContent = cacheFile.ReadFile();
        if (string.IsNullOrWhiteSpace(cachedContent) || cachedContent.Trim() == "null")
        {
            cacheFile.DeleteFile();
            cachedContent = null;
        }
        Response response = null;
        if (cachedContent is null || ProgramData.CheckCooldown(categoryNamespace, Cooldown))
        {
            response = await QueryGraphQL(categoryNamespace);
#if DEBUG
            if (response is null)
            {
                DebugForm.Current.Log("ES: QueryGraphQL returned null");
            }
#endif
            try
            {
                cacheFile.WriteFile(JsonConvert.SerializeObject(response, Formatting.Indented));
            }
            catch
            {
                // ignored
            }
        }
        else
            try
            {
                response = JsonConvert.DeserializeObject<Response>(cachedContent);
            }
            catch
            {
                cacheFile.DeleteFile();
            }

        if (response is null || response.Data?.Catalog is null)
            return dlcIds;
        List<Element> searchStore = [..response.Data.Catalog.SearchStore?.Elements ?? []];
        foreach (Element element in searchStore)
        {
            string title = element.Title;
            string product = element.CatalogNs?.Mappings is { Length: > 0 }
                ? element.CatalogNs.Mappings.First().PageSlug
                : null;
            string icon = PickKeyImageUrl(element.KeyImages, "DieselStoreFront");

            if (element.Items is null)
                continue;
            foreach (Item item in element.Items)
                dlcIds.Populate(item.Id, title, product, icon, null, element.Items.Length == 1);
        }

        List<Element> catalogOffers = [..response.Data.Catalog.CatalogOffers?.Elements ?? []];
        foreach (Element element in catalogOffers)
        {
            string title = element.Title;
            string product = element.CatalogNs?.Mappings is { Length: > 0 }
                ? element.CatalogNs.Mappings.First().PageSlug
                : null;
            string icon = PickKeyImageUrl(element.KeyImages, "Thumbnail");

            if (element.Items is null)
                continue;
            foreach (Item item in element.Items)
                dlcIds.Populate(item.Id, title, product, icon, item.Developer, element.Items.Length == 1);
        }

        return dlcIds;
    }

    private static string PickKeyImageUrl(KeyImage[] keyImages, string preferredType)
    {
        if (keyImages is null)
            return null;
        foreach (KeyImage keyImage in keyImages)
        {
            if (keyImage is null || keyImage.Type != preferredType || keyImage.Url is null)
                continue;
            return keyImage.Url.ToString();
        }
        return null;
    }

    private static string SanitizeCacheKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            return "_";
        char[] buffer = new char[key.Length];
        char[] invalid = System.IO.Path.GetInvalidFileNameChars();
        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            buffer[i] = c is '/' or '\\' or ':' || Array.IndexOf(invalid, c) >= 0 ? '_' : c;
        }
        return new string(buffer);
    }

    private static void Populate(
        this List<(string id, string name, string product, string icon, string developer)> dlcIds, string id,
        string title,
        string product, string icon, string developer, bool canOverwrite = false)
    {
        if (id == null)
            return;
        bool found = false;
        for (int i = 0; i < dlcIds.Count; i++)
        {
            (string id, string name, string product, string icon, string developer) app = dlcIds[i];
            if (app.id != id)
                continue;

            found = true;
            dlcIds[i] = canOverwrite
                ? (app.id, title ?? app.name, product ?? app.product, icon ?? app.icon, developer ?? app.developer)
                : (app.id, app.name ?? title, app.product ?? product, app.icon ?? icon, app.developer ?? developer);
            break;
        }

        if (!found)
            dlcIds.Add((id, title, product, icon, developer));
    }

    public static bool EpicBool = true;

    private static async Task<Response> QueryGraphQL(string categoryNamespace)
    {
        try
        {
            string encoded = HttpUtility.UrlEncode(categoryNamespace);
            Request request = new(encoded);
            string payload = JsonConvert.SerializeObject(request);
            using HttpContent content = new StringContent(payload);
            content.Headers.ContentType = new("application/json");
            HttpClient client = HttpClientManager.HttpClient;
            if (client is null)
            {
#if DEBUG
                DebugForm.Current.Log("ES: Client returned null");
#endif
                return null;
            }
            HttpResponseMessage httpResponse =
                await client.PostAsync(new Uri("https://launcher.store.epicgames.com/graphql"), content);
            _ = httpResponse.EnsureSuccessStatusCode();
            string response = await httpResponse.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<Response>(response);
        }
        catch
        {
            return null;
        }
    }
}