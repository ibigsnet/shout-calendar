# Shout Calendar

Dalamud plugin that watches FFXIV chat for event invites and keeps them on a month calendar until you accept them. Shout Calendar on its own does not send anything to other players. Shout Calendar Sync is a separate plugin.

The installer name is Shout Calendar. The window title is FFXIV Shout Calendar. The author is RifleJock.

License: GPL-3.0-or-later. See [LICENSE](LICENSE).

## Install from Dalamud

This is not on the official plugin list. A friend can add one custom repository URL. That URL is a plugin list, not a DLL or a zip.

In game:

1. `/xlsettings`
2. **Experimental**
3. Under **Custom Plugin Repositories**, paste:

   `https://raw.githubusercontent.com/ibigsnet/shout-calendar/main/pluginmaster.json`

4. Click **+**, leave **Enabled** checked, then **Save and Close**.
5. `/xlplugins`, search for **Shout Calendar**, and install it.

Dalamud API level 15. `/shoutcalendar` opens the calendar. `/shoutcalendar test` checks a line on your machine and does not send chat.

## What it does

A new install listens to Say, Shout, Yell, incoming tells, Free Company, Free Company announcements, and Novice Network. Other player chats are off until you check them. The Chats list starts collapsed.

With the aggressive filter on, a message is kept when two of a date, a time, and a place are present. Dates include `10/13/26`, today, tonight, and tomorrow. Places include a ward or plot, map coordinates, a world, and a place name from the game's own data. A time with no date lands on the day the message arrived. A place with no date sits under the month in **Needs a date**. A ward heard while you are in a city guesses that city's housing district. Edit changes the note, date (`yyyy-MM-dd`), time (`HH:mm`), and location.

Pending invites are orange. Accepted invites are green and leave the left list. Decline and Delete remove an invite. Edit and Delete still work after you accept. Clear local, Clear local accepted, and Clear local unaccepted ask Yes or No first and leave shared invites alone. The Sync tab has Clear sync accepted and Clear sync unaccepted. Those remove shared invites on this computer from every server calendar that is open, and the question names those servers. The relay keeps them for other people. Delete on a shared invite does the same for that one row, and that row does not come back.

Accepted and pending invites are saved across restarts. **Hold unaccepted for (days)** defaults to 1. Accepted invites stay until you delete them.

**Aggressive filter** is on by default. A new line is kept only when two of a date, a time, and a place are present. Now, right now, and a line that ends with right count as the current time. Lines from the same player on shout or yell, close together, are read as one invite. A shout that names a world is filed on that world. A data center name, such as Crystal, is not a world.

**Add event** on Pending creates an invite on this computer only. Paste the details and use Read details, or type the date (`yyyy-MM-dd`), time (`HH:mm`), and location yourself. A hand-added event is not shared.

Web addresses and `discord.gg` invites in an event are buttons. Opening one asks first. Remember this choice is on that prompt and in Settings.

**Alarms** ring a chat sound, `<se.1>` through `<se.16>`, when an accepted event's clock arrives. **Minutes before** defaults to 15. An event accepted inside that window rings then. One that started longer ago does not. **Alarm unaccepted events** is off until checked. Test plays the selected sound.

The left side has **Pending**, **Settings**, **Resets**, and **Colors**. Sync settings is a tab on that row when Sync is attached. Pending is the invite list, plus shared invites that are still waiting. Settings holds the hold time, the aggressive filter, Informedaholic, link choice, chats, and alarms. Informedaholic is off. When it is on, kept invites are accepted for you. Resets lists the clocks. Jumbo Cactpot, the weekly reset, and A Nocturne for Heroes start on. The other rows, including Wondrous Tails, start off. The month and year in the header are lists. The year list opens on the current year. Today is a light wash. The other colors are unchanged until you edit them.

Each alarm has its own `<se.#>` and an optional WAV file. An empty file uses the chat sound. Reset alarms are separate from accepted and pending invites. A weekday such as "next Tuesday", and a glued ward such as `W3P26`, count toward the date and the place.

## Official plugin list

The official list is a separate review. It is a pull request to [goatcorp/DalamudPluginsD17](https://github.com/goatcorp/DalamudPluginsD17) with a `manifest.toml`, a square `icon.png` (64 to 512 pixels), and a pass through the testing track. That pull request has not been opened.

## Build

```bash
dotnet build -c Release
```

Dalamud's packager writes `src/ShoutCalendar/bin/Release/ShoutCalendar/latest.zip`.
