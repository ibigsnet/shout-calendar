namespace ShoutCalendar.Core;

/// <summary>Joins a few lines from one player on one channel so a split invite can be read whole.</summary>
public sealed class ChatBurst
{
    public const int WindowSeconds = 60;

    public const int MaxLines = 4;

    private readonly List<string> lines = new();
    private string sender = "";
    private int channel;
    private DateTimeOffset lastAt;
    private string? keptId;

    public string Push(string? sender, int channel, DateTimeOffset when, string? text, out string? replaceId)
    {
        var who = SenderName.Clean(sender);
        var line = Collapse(text);
        var continues = this.lines.Count > 0
            && who.Length > 0
            && string.Equals(this.sender, who, StringComparison.OrdinalIgnoreCase)
            && Similar(this.channel, channel)
            && when >= this.lastAt
            && when - this.lastAt <= TimeSpan.FromSeconds(WindowSeconds)
            && this.lines.Count < MaxLines;
        if (!continues)
        {
            this.lines.Clear();
            this.keptId = null;
            this.sender = who;
            this.channel = channel;
        }

        if (line.Length > 0)
            this.lines.Add(line);
        this.lastAt = when;
        replaceId = continues ? this.keptId : null;
        return string.Join(' ', this.lines);
    }

    public void Remember(string? id) => this.keptId = id;

    private static bool Similar(int left, int right)
    {
        if (left == right)
            return true;
        return SharePolicy.IsShareable(left) && SharePolicy.IsShareable(right);
    }

    private static string Collapse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
