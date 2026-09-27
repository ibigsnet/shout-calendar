# Open questions

These are not decided. The plugin does not pick an answer for any of them.

The built behavior is only the part that was specified: a shout with an explicit clock time and a ward number and/or an English world or data-center name becomes one calendar entry on the UTC date of the shout timestamp. One clock time is shown on that day and is not ongoing. Two ordered clock times are ongoing only while an evaluation instant's UTC minute is inside that interval.

## Plugin display name and author

What display name and author should the manifest use? The current values are provisional (`Shout Calendar (provisional)` / `provisional`). The assembly internal name is `ShoutCalendar`.

## Publish to GitHub or the official Dalamud repository

Should this be published to GitHub, or submitted to the official Dalamud plugin repository? Neither has been done. No GitHub URL is set.

## Which AI model, if any, should interpret shouts that lack an explicit time and a ward or server

Shouts that do not state an explicit clock time and a ward number or a world or data-center name are ignored. No model is called. Which AI model, if any, should interpret those shouts?

## How to treat ST/ET/PT labels, Eorzea time, and relative times

How should ST, ET, and PT labels be treated? How should Eorzea time be treated? How should relative times such as "tonight" and "in 20 minutes" be treated? Those labels are not converted. "tonight", "in 20 minutes", and bell times are not read as a clock.

## How long a one-time shout stays ongoing

How long should a shout with only one clock time stay ongoing? It stays visible on its day and is not marked ongoing. No duration is invented.

## Whether a repeated shout is a duplicate

Is a repeated shout a duplicate? Two live shouts are both kept. Re-reading the same on-disk log line (same timestamp, channel, and message) is skipped so a restart does not copy that line again. Whether those should be one entry is still open.

## Place kinds beyond ward numbers and server names

Which place kinds beyond ward numbers and world or data-center names should count as a place on their own? A plot, apartment, room, house, cottage, or the word subdivision is kept when a ward or server is also present. Those words alone do not add an entry. Chinese and Korean world names are not matched.

## Channels besides shout

Which channels besides shout should be harvested? Only the shout channel is harvested. Say, yell, party, and other channels are not added.

## Whether the calendar is this client only or shared

Is the calendar this client only, or shared with other players? It is stored in this client's Dalamud plugin config. Nothing is shared.

## Developer mode

Dalamud loads `DevPluginLoadLocations` only when `DevMode` is true. This machine's `DevMode` is false and was not changed. Should developer mode be turned on so the dev-plugin entry loads?

## End clock earlier than the start clock

When a shout's end clock is earlier than its start clock, should that interval cross midnight? It is not marked ongoing.

## More than two clock times

When a shout states more than two clock times, which pair is the interval? The entry is placed on the first clock time and is not marked ongoing.
