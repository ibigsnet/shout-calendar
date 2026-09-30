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

    [Fact]
    public void AVenueIsNotATellTarget()
    {
        var place = "ward 4, plot 43, Diabolos, The Goblet";
        Assert.False(SenderName.IsCharacter(place));
        Assert.False(SenderName.IsCharacter("The Goblet"));
        Assert.False(SenderName.IsCharacter("Lavender Beds"));
        Assert.Equal("", TellDraft.Command(place, "", "Diabolos", ""));
        Assert.True(SenderName.IsCharacter("Mina Willow"));
        Assert.True(SenderName.IsCharacter("Y'shtola Rhul"));
        Assert.Equal(
            "/tell Mina Willow@Diabolos Hey, I had a question about that invite. What location will we be meeting up at?",
            TellDraft.Command("Mina Willow", "Diabolos", "Diabolos", ""));
    }
}
