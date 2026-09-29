using ShoutCalendar.Core;

namespace ShoutCalendar.Tests;

public class TellDraftTests
{
    [Fact]
    public void AGluedWorldBecomesTheTellTarget()
    {
        var (name, world) = SenderName.TellTarget("Salt AeosFaerie", "Faerie");
        Assert.Equal("Salt Aeos", name);
        Assert.Equal("Faerie", world);
        var command = TellDraft.Command("Salt AeosFaerie", "", "", "\uE075\uE074\uE075\uE07E");
        Assert.Equal(
            "/tell Salt Aeos@Faerie Hey, I had a question about that invite, EDEN. What location will we be meeting up at?",
            command);
        Assert.Equal(("Salt Aeos", "Faerie"), SenderName.TellTarget("Salt Aeos", "Faerie"));
        Assert.Equal(("Y'shtola Rhul", ""), SenderName.TellTarget("Y'shtola Rhul", ""));
    }
}
