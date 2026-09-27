using System.Runtime.InteropServices;

namespace ShoutCalendar;

/// <summary>
/// Plays a user WAV when one is set. Anything else falls back to the chat sound.
/// A Linux path is also tried as a Wine Z: path.
/// </summary>
internal static class AlarmPlayback
{
    private const uint FileName = 0x00020000;
    private const uint Async = 0x0001;
    private const uint NoDefault = 0x0002;

    public static bool TryPlayFile(string? path)
    {
        var resolved = Resolve(path);
        if (resolved is null || !resolved.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            return PlaySound(resolved, 0, FileName | Async | NoDefault);
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    public static string? Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var trimmed = path.Trim();
        if (File.Exists(trimmed))
            return trimmed;
        if (trimmed.StartsWith('/'))
        {
            var wine = "Z:" + trimmed.Replace('/', '\\');
            if (File.Exists(wine))
                return wine;
        }

        return null;
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PlaySound(string pszSound, nint hmod, uint fdwSound);
}