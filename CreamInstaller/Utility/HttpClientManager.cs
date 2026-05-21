using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
#if DEBUG
using CreamInstaller.Forms;
#endif

namespace CreamInstaller.Utility;

internal static class HttpClientManager
{
    private static readonly object SetupLock = new();

    internal static HttpClient HttpClient;

    private static readonly ConcurrentDictionary<string, string> HttpContentCache = new();

    internal static void Setup()
    {
        lock (SetupLock)
        {
            HttpClient old = HttpClient;
            HttpClient fresh = new() { Timeout = TimeSpan.FromSeconds(30) };
            if (CreamInstaller.Platforms.Epic.EpicStore.EpicBool)
            {
                fresh.DefaultRequestHeaders.UserAgent.Add(new("EpicGamesLauncher", "18.9.0-45233261+++Portal+Release-Live"));
                CreamInstaller.Platforms.Epic.EpicStore.EpicBool = false;
            }
            else
            {
                fresh.DefaultRequestHeaders.UserAgent.Add(new(Program.Name, Program.Version));
            }
            fresh.DefaultRequestHeaders.AcceptLanguage.Add(new(CultureInfo.CurrentCulture.ToString()));

            // Publish the new client before disposing the old one so concurrent
            // readers never observe a disposed HttpClient.
            HttpClient = fresh;
            old?.Dispose();
        }
    }

    internal static async Task<string> EnsureGet(string url)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            using HttpResponseMessage response =
                await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode is HttpStatusCode.NotModified &&
                HttpContentCache.TryGetValue(url, out string content))
                return content;
            _ = response.EnsureSuccessStatusCode();
            content = await response.Content.ReadAsStringAsync();
            HttpContentCache[url] = content;
            return content;
        }
        catch (HttpRequestException e)
        {
            if (e.StatusCode != HttpStatusCode.TooManyRequests)
            {
#if DEBUG
                DebugForm.Current.Log("Get request failed to " + url + ": " + e, LogTextBox.Warning);
#endif
                return null;
            }
#if DEBUG
            DebugForm.Current.Log("Too many requests to " + url, LogTextBox.Error);
#endif
            // do something special?
            return null;
        }
#if DEBUG
        catch (Exception e)
        {
            DebugForm.Current.Log("Get request failed to " + url + ": " + e, LogTextBox.Warning);
            return null;
        }
#else
        catch
        {
            return null;
        }
#endif
    }

    internal static async Task<Image> GetImageFromUrl(string url)
    {
        try
        {
            // Copy into a MemoryStream so the network stream can be released
            // immediately. `new Bitmap(stream)` would otherwise hold the
            // network stream open for the lifetime of the Bitmap.
            HttpClient client = HttpClient;
            if (client is null)
                return null;
            await using Stream net = await client.GetStreamAsync(new Uri(url));
            MemoryStream buffer = new();
            await net.CopyToAsync(buffer);
            buffer.Position = 0;
            return new Bitmap(buffer);
        }
        catch
        {
            return null;
        }
    }

    internal static void Dispose()
    {
        lock (SetupLock)
        {
            HttpClient client = Interlocked.Exchange(ref HttpClient, null);
            client?.Dispose();
        }
    }
}