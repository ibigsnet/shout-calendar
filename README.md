# Shout Calendar (provisional)

Local Dalamud plugin for this machine. A shout is added to a month calendar when the message states an explicit clock time and a ward number and/or an FFXIV world or data-center name.

Open the window with `/shoutcalendar`, or from the plugin's entry in the plugin installer.

Name, author, and the behaviors in `QUESTIONS.md` are not decided. The plugin does not call an AI model.

Build:

```
dotnet build -c Release
```

The plugin project is `src/ShoutCalendar`. Dalamud API level 15.
