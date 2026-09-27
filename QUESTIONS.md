# Open questions

The display name and author are still provisional (`Shout Calendar (provisional)` / `provisional`). The assembly internal name is `ShoutCalendar`.

## Publish to GitHub or the official Dalamud repository

The source is on GitHub, and a custom repository URL installs it from Dalamud. It has not been submitted to the official Dalamud plugin repository.

## Which AI model, if any, should interpret shouts that lack an explicit time and a ward or server

No AI model is called. A message is kept only when the text has a time or a place the plugin already recognizes.

## How to treat ST/ET/PT labels, Eorzea time, and relative times

ST, ET, and PT are not converted. Eorzea time is not converted. Today and tonight use the message's date. Tomorrow is the next day. "in 20 minutes" is not read as a clock.

## How long a one-time shout stays ongoing

A shout with one clock time stays on its day and is not marked ongoing. No duration is invented.

## Whether a repeated shout is a duplicate

Two live shouts are both kept. Re-reading the same on-disk log line is skipped so a restart does not copy that line again.

## Place kinds beyond ward numbers and server names

Wards, plots, map coordinates, worlds, data centers, and place names from the game data count. A plot, apartment, room, house, cottage, or the word subdivision is kept when a ward or server is also present. Chinese and Korean world names are not matched.

## Channels besides shout

Say, Shout, Yell, incoming tells, Free Company, Free Company announcements, and Novice Network start on. Party, alliance, outgoing tells, linkshells, and cross-world linkshells start off. Whether more channels should start on is still open.

## Whether the calendar is this client only or shared

It is stored in this client only, in Dalamud's plugin config. Nothing is shared.

## Developer mode

DevMode is only for loading a DLL from disk while developing. A custom repository install does not need DevMode.

## End clock earlier than the start clock

When the end clock is earlier than the start clock, the interval does not cross midnight and is not marked ongoing.

## More than two clock times

When a shout states more than two clock times, the entry uses the first clock time and is not marked ongoing.
