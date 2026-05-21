using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace CreamInstaller.Utility;

internal static class Diagnostics
{
    private static string nppPath;

    private static string NppPath
    {
        get
        {
            nppPath ??= Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Notepad++", "", null) as string;
            nppPath ??= Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432NODE\Notepad++", "", null) as string;
            return nppPath;
        }
    }

    internal static string GetNotepadPath()
    {
        string npp = NppPath + @"\notepad++.exe";
        return npp.FileExists() ? npp : Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\notepad.exe";
    }

    internal static void OpenFileInNotepad(string path)
    {
        string npp = NppPath + @"\notepad++.exe";
        if (npp.FileExists())
            StartProcess(npp, path);
        else
            StartProcess("notepad.exe", path);
    }

    internal static void OpenDirectoryInFileExplorer(string path) => StartProcess("explorer.exe", path);

    internal static void OpenUrlInInternetBrowser(string url)
    {
        // Only allow http(s) URLs; UseShellExecute would happily launch
        // javascript:, file:, ms-..., or arbitrary file paths otherwise.
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return;
        try
        {
            using Process _ = Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch
        {
            // Browser launch failures are non-fatal.
        }
    }

    private static void StartProcess(string fileName, string argument)
    {
        // Use ArgumentList so the runtime handles quoting/escaping — paths with
        // spaces or quotes were previously passed as a raw command line and got
        // truncated at the first whitespace.
        ProcessStartInfo info = new() { FileName = fileName, UseShellExecute = false };
        if (argument is not null)
            info.ArgumentList.Add(argument);
        try
        {
            using Process _ = Process.Start(info);
        }
        catch
        {
            // Launch failures should not crash the host app.
        }
    }

    internal static string ResolvePath(this string path)
    {
        if (path is null || !path.FileExists() && !path.DirectoryExists())
            return null;
        DirectoryInfo info = new(path);
        if (info.Parent is null)
            return info.Name.ToUpperInvariant();
        string parent = ResolvePath(info.Parent.FullName);
        string name = info.Parent.GetFileSystemInfos(info.Name)[0].Name;
        return parent is null ? name : Path.Combine(parent, name);
    }
}