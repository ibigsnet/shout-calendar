namespace ShoutCalendar.Core;

public enum ClearTarget
{
    All,
    Accepted,
    Unaccepted,
}

/// <summary>Yes/no confirmation before clearing shout history.</summary>
public sealed class ClearPrompt
{
    public bool IsOpen { get; private set; }

    public ClearTarget Target { get; private set; }

    public void Ask() => this.Ask(ClearTarget.All);

    public void Ask(ClearTarget target)
    {
        this.Target = target;
        this.IsOpen = true;
    }

    public void AnswerNo() => this.IsOpen = false;

    public string Question => this.Target switch
    {
        ClearTarget.Accepted => "Clear every accepted event?",
        ClearTarget.Unaccepted => "Clear every unaccepted invite?",
        _ => "Clear every detected shout, including accepted events?",
    };

    public void AnswerYes(CalendarLog log)
    {
        switch (this.Target)
        {
            case ClearTarget.Accepted:
                log.ClearAccepted();
                break;
            case ClearTarget.Unaccepted:
                log.ClearUnaccepted();
                break;
            default:
                log.Clear();
                break;
        }

        this.IsOpen = false;
    }
}
