# Shout Calendar (provisional)

Dalamud plugin that watches FFXIV chat for event invites and keeps them on a month calendar until you accept them. Nothing is accepted for you, and nothing is sent to other players.

The display name and author are still provisional.

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

A message is kept when it has a time or a place: a clock time, a date such as `10/13/26`, today, tonight, or tomorrow, a ward or plot, map coordinates, a world or data center, or a place name from the game's own data. A time with no place lands on the day the message arrived. A place with no time sits under the month in **Needs a date**. A ward heard while you are in a city guesses that city's housing district. Edit changes the note, date (`yyyy-MM-dd`), time (`HH:mm`), and location.

Pending invites are orange. Accepted invites are green and leave the left list. Decline and Delete remove an invite. Edit and Delete still work after you accept. Clear all, Clear accepted, and Clear unaccepted each ask Yes or No first.

Accepted and pending invites are saved across restarts. **Hold unaccepted for (days)** defaults to 14. Accepted invites stay until you delete them.

**Aggressive filter**, off by default, keeps a new line only when two of a date, a time, and a place are present. On a pending invite, Decline and Delete both remove it.

**Alarms** ring a chat sound, `<se.1>` through `<se.16>`, when an accepted event's clock arrives. **Minutes before** is 0 at that clock. **Alarm unaccepted events** is off until checked, and it has its own sound. Test plays the selected sound.

## Official plugin list

The official list is a separate review. It is a pull request to [goatcorp/DalamudPluginsD17](https://github.com/goatcorp/DalamudPluginsD17) with a `manifest.toml`, a square `icon.png` (64 to 512 pixels), and a pass through the testing track. That pull request has not been opened.

## Build

```bash
dotnet build -c Release
```

Dalamud's packager writes `src/ShoutCalendar/bin/Release/ShoutCalendar/latest.zip`.
