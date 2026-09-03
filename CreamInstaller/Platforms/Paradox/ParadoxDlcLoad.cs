using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CreamInstaller.Platforms.Paradox;

/// <summary>
///     Pure parsing/transformation helpers for the Paradox Launcher's on-disk formats, kept free of any file or UI
///     dependency so the behavior is easy to reason about. <see cref="ParadoxGame" /> supplies the file contents.
/// </summary>
internal static partial class ParadoxDlcLoad
{
    internal const string DisabledDlcsKey = "disabled_dlcs";

    [GeneratedRegex(@"steam_id\s*=\s*""?(\d+)""?", RegexOptions.IgnoreCase)]
    private static partial Regex SteamIdRegex();

    /// <summary>
    ///     Reads the <c>steam_id</c> of a Paradox <c>.dlc</c> descriptor, whose contents look like
    ///     <c>name = "Waking the Tiger"</c> / <c>steam_id = "702350"</c>.
    /// </summary>
    internal static string ParseSteamId(string dlcDescriptor)
    {
        if (dlcDescriptor is null)
            return null;
        Match match = SteamIdRegex().Match(dlcDescriptor);
        return match.Success && int.TryParse(match.Groups[1].Value, out int appId) && appId > 0 ? "" + appId : null;
    }

    /// <summary>
    ///     Expands the placeholders the launcher uses in <c>launcher-settings.json</c> paths, e.g.
    ///     <c>%USER_DOCUMENTS%/Paradox Interactive/Hearts of Iron IV</c>. Returns null when a placeholder could not
    ///     be resolved, so callers fall back rather than creating a nonsense path.
    /// </summary>
    internal static string ExpandPathTokens(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        path = path
            .Replace("%USER_DOCUMENTS%", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                StringComparison.OrdinalIgnoreCase)
            .Replace("%USER_HOME%", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                StringComparison.OrdinalIgnoreCase)
            .Replace("%LOCAL_APPLICATION_DATA%",
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                StringComparison.OrdinalIgnoreCase)
            .Replace("%APPLICATION_DATA%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                StringComparison.OrdinalIgnoreCase);
        path = Environment.ExpandEnvironmentVariables(path).Replace('/', '\\').TrimEnd('\\');
        return path.Length < 1 || path.Contains('%', StringComparison.Ordinal) ? null : path;
    }

    /// <summary>
    ///     Rewrites the <c>disabled_dlcs</c> array of a <c>dlc_load.json</c> document so that every selected DLC is
    ///     allowed to load and every deselected one stays disabled. Entries with no matching <c>.dlc</c> descriptor
    ///     are left alone, as are all other properties of the document (<c>enabled_mods</c> in particular).
    /// </summary>
    /// <returns>Whether anything changed; <paramref name="updated" /> is only set when it did.</returns>
    internal static bool TryUpdateDisabledDlcs(string original, IReadOnlyDictionary<string, string> pathsBySteamId,
        IEnumerable<(string id, bool enabled)> dlc, out string updated, out int unlocked, out int locked)
    {
        updated = null;
        unlocked = 0;
        locked = 0;
        if (original is null || pathsBySteamId is null || pathsBySteamId.Count < 1)
            return false;
        JObject document;
        try
        {
            document = JObject.Parse(original);
        }
        catch (JsonException)
        {
            return false;
        }

        HashSet<string> disabled = new(
            document[DisabledDlcsKey] is JArray array
                ? array.Select(token => token.ToString()).Where(path => !string.IsNullOrWhiteSpace(path))
                : [], StringComparer.OrdinalIgnoreCase);
        foreach ((string id, bool enabled) in dlc)
        {
            if (id is null || !pathsBySteamId.TryGetValue(id, out string path))
                continue;
            if (enabled)
            {
                if (disabled.Remove(path))
                    unlocked++;
            }
            else if (disabled.Add(path))
                locked++;
        }

        if (unlocked < 1 && locked < 1)
            return false;
        document[DisabledDlcsKey] = new JArray(disabled.OrderBy(path => path, StringComparer.Ordinal));
        updated = document.ToString(Formatting.Indented);
        return true;
    }
}
