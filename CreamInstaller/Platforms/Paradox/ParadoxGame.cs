using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CreamInstaller.Forms;
using CreamInstaller.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CreamInstaller.Platforms.Paradox;

/// <summary>
///     Support for Paradox Interactive games (Hearts of Iron IV, Europa Universalis IV, Crusader Kings III,
///     Stellaris, Victoria 3, ...), which are launched through the Paradox Launcher.
/// </summary>
/// <remarks>
///     Patching the game's Steamworks DLL is not enough for these games: the launcher is a separate process that
///     decides which DLC the game is allowed to load and writes that decision to <c>dlc_load.json</c> in the game's
///     data directory. Every DLC the launcher considers unowned ends up in that file's <c>disabled_dlcs</c> array,
///     and the game obeys the file regardless of what its own (patched) Steamworks DLL reports. This class locates
///     that file through the game's <c>launcher-settings.json</c> and keeps the disabled list in sync with the DLC
///     the user selected.
/// </remarks>
internal static class ParadoxGame
{
    private const string LauncherSettings = "launcher-settings.json";
    private const string Dowser = "dowser.exe";
    private const string DlcLoad = "dlc_load.json";
    private const string DlcLoadBackup = "dlc_load.json.creaminstaller.bak";
    private const string DefaultDlcPath = "dlc";

    /// <summary>
    ///     Whether the given game root directory belongs to a game distributed with the Paradox Launcher.
    /// </summary>
    internal static bool IsParadoxGame(this string rootDirectory)
        => rootDirectory is not null
           && ((rootDirectory + @"\" + LauncherSettings).FileExists() || (rootDirectory + @"\" + Dowser).FileExists());

    /// <summary>
    ///     The game's data directory, e.g. <c>%USERPROFILE%\Documents\Paradox Interactive\Hearts of Iron IV</c>.
    ///     Read from <c>launcher-settings.json</c> so redirected Documents folders and custom paths are honored.
    /// </summary>
    internal static string GetDataDirectory(string rootDirectory, string gameName)
    {
        string fromSettings = ParadoxDlcLoad.ExpandPathTokens(ReadLauncherSetting(rootDirectory, "gameDataPath"));
        if (fromSettings is not null && fromSettings.DirectoryExists())
            return fromSettings;
        if (string.IsNullOrWhiteSpace(gameName))
            return fromSettings;
        string fallback = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                          + @"\Paradox Interactive\" + gameName;
        return fallback.DirectoryExists() ? fallback : fromSettings;
    }

    internal static string GetDlcLoadPath(string rootDirectory, string gameName)
    {
        string dataDirectory = GetDataDirectory(rootDirectory, gameName);
        return dataDirectory is null ? null : dataDirectory + @"\" + DlcLoad;
    }

    private static string ReadLauncherSetting(string rootDirectory, string key)
    {
        if (rootDirectory is null)
            return null;
        string settings = (rootDirectory + @"\" + LauncherSettings).ReadFile();
        if (settings is null)
            return null;
        try
        {
            return JObject.Parse(settings)[key]?.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Maps each Steam DLC app id shipped in the game's <c>dlc</c> directory to the game-root-relative path of
    ///     its <c>.dlc</c> descriptor, which is the exact form <c>dlc_load.json</c> uses
    ///     (e.g. <c>dlc/dlc022_waking_the_tiger/dlc022.dlc</c>).
    /// </summary>
    internal static Dictionary<string, string> GetDlcPathsBySteamId(string rootDirectory)
    {
        Dictionary<string, string> paths = new(StringComparer.Ordinal);
        if (rootDirectory is null)
            return paths;
        string dlcPath = ParadoxDlcLoad.ExpandPathTokens(ReadLauncherSetting(rootDirectory, "dlcPath"))
                         ?? DefaultDlcPath;
        string dlcDirectory = Path.IsPathRooted(dlcPath) ? dlcPath : rootDirectory + @"\" + dlcPath;
        foreach (string file in dlcDirectory.EnumerateDirectory("*.dlc", true))
        {
            if (Program.Canceled)
                break;
            string steamId = ParadoxDlcLoad.ParseSteamId(file.ReadFile());
            if (steamId is null)
                continue;
            try
            {
                paths[steamId] = Path.GetRelativePath(rootDirectory, file).Replace('\\', '/');
            }
            catch (ArgumentException)
            {
                // A descriptor outside the game directory has no relative form dlc_load.json could name.
            }
        }

        return paths;
    }

    /// <summary>
    ///     Rewrites <c>disabled_dlcs</c> in the game's <c>dlc_load.json</c> so that every selected DLC is allowed to
    ///     load and every deselected DLC stays disabled. The original file is backed up once, before the first
    ///     modification, so <see cref="RestoreDlcLoad" /> can put it back on uninstallation.
    /// </summary>
    internal static async Task<bool> UpdateDlcLoad(Selection selection, InstallForm installForm = null)
    {
        string rootDirectory = selection.RootDirectory;
        string name = selection.Name;
        // Snapshot the selection before going off-thread; Enabled reads a tree node's checked state.
        List<(string id, bool enabled)> dlc = selection.DLC.Select(d => (d.Id, d.Enabled)).ToList();
        return await Task.Run(() =>
        {
            string dlcLoad = GetDlcLoadPath(rootDirectory, name);
            if (dlcLoad is null || !dlcLoad.FileExists())
                return false;
            string original = dlcLoad.ReadFile();
            if (original is null)
                return false;
            if (!ParadoxDlcLoad.TryUpdateDisabledDlcs(original, GetDlcPathsBySteamId(rootDirectory), dlc,
                    out string updated, out int unlocked, out int locked))
                return false;
            string backup = Path.GetDirectoryName(dlcLoad) + @"\" + DlcLoadBackup;
            if (!backup.FileExists())
            {
                backup.WriteFile(original);
                installForm?.UpdateUser($"Backed up Paradox DLC load order: {Path.GetFileName(backup)}",
                    LogTextBox.Action, false);
            }

            dlcLoad.WriteFile(updated);
            installForm?.UpdateUser(
                $"Updated Paradox DLC load order for {name}: enabled {unlocked} DLC, disabled {locked} DLC",
                LogTextBox.Action, false);
            return true;
        });
    }

    /// <summary>
    ///     Restores the <c>dlc_load.json</c> backup taken by <see cref="UpdateDlcLoad" />, if there is one.
    /// </summary>
    internal static async Task<bool> RestoreDlcLoad(Selection selection, InstallForm installForm = null)
    {
        string rootDirectory = selection.RootDirectory;
        string name = selection.Name;
        return await Task.Run(() =>
        {
            string dlcLoad = GetDlcLoadPath(rootDirectory, name);
            if (dlcLoad is null)
                return false;
            string backup = Path.GetDirectoryName(dlcLoad) + @"\" + DlcLoadBackup;
            if (!backup.FileExists())
                return false;
            dlcLoad.DeleteFile(true);
            backup.MoveFile(dlcLoad);
            installForm?.UpdateUser($"Restored Paradox DLC load order: {Path.GetFileName(dlcLoad)}", LogTextBox.Action,
                false);
            return true;
        });
    }

    /// <summary>
    ///     Manual counterpart of <see cref="UpdateDlcLoad" />, used by the selection tree's context menu.
    /// </summary>
    internal static async Task RepairDlcLoad(Form form, Selection selection)
    {
        using DialogForm dialogForm = new(form);
        string dlcLoad = GetDlcLoadPath(selection.RootDirectory, selection.Name);
        if (dlcLoad is null || !dlcLoad.FileExists())
        {
            _ = dialogForm.Show(SystemIcons.Error,
                $"Could not find {DlcLoad} for {selection.Name}."
                + "\n\nStart the game through the Paradox Launcher at least once, then try again.",
                customFormText: "Paradox Launcher");
            return;
        }

        bool repaired = await UpdateDlcLoad(selection);
        _ = dialogForm.Show(SystemIcons.Information,
            repaired
                ? $"The Paradox DLC load order for {selection.Name} was repaired."
                  + $"\n\nThe original {DlcLoad} was backed up as {DlcLoadBackup}."
                : $"The Paradox DLC load order for {selection.Name} did not need to be repaired.",
            customFormText: "Paradox Launcher");
    }
}
