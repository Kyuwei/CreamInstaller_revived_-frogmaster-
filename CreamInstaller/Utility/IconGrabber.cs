using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace CreamInstaller.Utility;

internal static class IconGrabber
{
    internal const string SteamAppImagesPath =
        "https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/";

    private const string GoogleFaviconsApiUrl = "https://www.google.com/s2/favicons";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    internal static Icon ToIcon(this Image image)
    {
        // Bitmap.GetHicon hands back an unmanaged HICON that Icon.FromHandle does
        // not own. Clone the Icon into a managed copy and immediately release the
        // native handle so the caller can dispose normally without leaking a GDI
        // handle per dialog.
        using Bitmap dialogIconBitmap = new(image, new(image.Width, image.Height));
        IntPtr hIcon = dialogIconBitmap.GetHicon();
        try
        {
            using Icon native = Icon.FromHandle(hIcon);
            return (Icon)native.Clone();
        }
        finally
        {
            _ = DestroyIcon(hIcon);
        }
    }

    internal static string GetDomainFaviconUrl(string domain, int size = 16) =>
        GoogleFaviconsApiUrl + $"?domain={Uri.EscapeDataString(domain ?? "")}&sz={size}";

    internal static Image GetFileIconImage(this string path)
    {
        if (!path.FileExists())
            return null;
        using Icon icon = Icon.ExtractAssociatedIcon(path);
        return icon?.ToBitmap();
    }

    internal static Image GetNotepadImage() => GetFileIconImage(Diagnostics.GetNotepadPath());

    internal static Image GetCommandPromptImage() => GetFileIconImage(Environment.SystemDirectory + @"\cmd.exe");

    internal static Image GetFileExplorerImage() =>
        GetFileIconImage(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\explorer.exe");
}