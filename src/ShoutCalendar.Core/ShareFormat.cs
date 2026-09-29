namespace ShoutCalendar.Core;

/// <summary>
/// Generation of the rules that parsed a shared shout.
/// Raise <see cref="Current"/> and <see cref="Minimum"/> together when a parse fix
/// should replace rows made by older plugins.
/// </summary>
public static class ShareFormat
{
    public const int Legacy = 0;

    public const int Current = 1;

    /// <summary>Unstamped rows are still shown. Raise this when the relay stores <see cref="Current"/>.</summary>
    public const int Minimum = 0;

    public static bool Accepts(int format) => format >= Minimum;
}
