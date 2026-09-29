namespace ShoutCalendar.Core;

/// <summary>A tell the player can send to the person who shared an invite.</summary>
public static class TellDraft
{
    public static string Command(string? sender, string? speakerWorld, string? eventWorld, string? title)
    {
        var (name, world) = SenderName.TellTarget(sender, speakerWorld);
        if (world.Length == 0 && PlayableWorlds.TryNamedWorld(eventWorld, out var named))
            world = named;
        if (name.Length == 0)
            return "";
        var who = world.Length > 0 ? $"{name}@{world}" : name;
        var subject = EventTitle.Readable(title);
        if (subject.Length == 0)
            subject = "that invite";
        return $"/tell {who} Hey, I had a question about that invite, {subject}. What location will we be meeting up at?";
    }
}
