using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CreamInstaller.Utility;
using Microsoft.Win32;

namespace CreamInstaller.Platforms.Ubisoft;

internal static class UbisoftLibrary
{
    internal static async Task<List<(string gameId, string name, string gameDirectory)>> GetGames()
        => await Task.Run(() =>
        {
            List<(string gameId, string name, string gameDirectory)> games = new();
            // RegistryKey holds an unmanaged HKEY; both the parent and every
            // subkey opened inside the loop are now disposed via `using`. The
            // previous static-property pattern reopened the parent on every
            // call and never closed it, leaking a registry handle per scan.
            using RegistryKey installsKey =
                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
            if (installsKey is null)
                return games;
            foreach (string gameId in installsKey.GetSubKeyNames())
            {
                using RegistryKey installKey = installsKey.OpenSubKey(gameId);
                string installDir = installKey?.GetValue("InstallDir")?.ToString()?.ResolvePath();
                if (installDir is null || games.Any(g => g.gameId == gameId))
                    continue;
                string name;
                try { name = new DirectoryInfo(installDir).Name; }
                catch { name = Path.GetFileName(installDir) ?? gameId; }
                games.Add((gameId, name, installDir));
            }

            return games;
        });
}