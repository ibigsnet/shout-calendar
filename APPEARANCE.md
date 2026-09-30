# Calendar appearance and sync guide

## Try these looks

Open **Appearance** in the left panel. On a short display, use **Panels / settings** first.

| Preset | Event blocks | Overnight events | Shading | Good starting point |
| --- | --- | --- | --- | --- |
| Minimal | Flat | Separate chips on each day | None | The least calendar-like treatment |
| Classic | Flat | Bars across days | None | Familiar layout without gradients |
| Soft | Flat | Bars across days | Gentle | A little depth without large nested blocks |
| Layered | Nested by time containment | Bars across days | Very light | Experimenting with overlapping events |

Presets change presentation, not invitations, subscriptions, colors, or text-contrast overrides. You can mix the individual options after choosing a preset. Nesting means one time range contains another; it does **not** mean the events share an organizer or venue.

## Overnight styles

- **Spanning bars:** one bar across the covered days. Useful when scanning a busy month.
- **Separate day chips:** each day gets its own slice, with the appropriate clock label. This uses no connector.
- **Straight arrows:** connect the visible start and continuation chips.
- **Curved arrows:** the same connection with a curved route.

An arrow is drawn only when both chips are visible. Overflow, scrolling, or the edge of the viewed range may hide a connector; the event remains in the day's full list. The connector identifies a particular occurrence so separate weeks do not get linked. Events ending at exactly midnight remain visually on the starting day. Longer game-schedule spans keep their bars independently of the invitation setting.

## Colors without clutter

Expand **Appearance > Colors** to see the swatches. The section starts collapsed; there is no separate Colors tab. Select a swatch or its label to open its color editor. **Auto / Light / Dark** text contrast lives inside that editor, rather than adding a checkbox beside every color.

Auto chooses readable text against the fill over a dark calendar surface. Light/Dark are explicit overrides. Previous Flip text preferences migrate to the corresponding explicit choice. Existing custom event colors still get automatic contrast.

**Shading** is separate from color: zero is flat; 0.15–0.30 is a useful subtle range. The Appearance preview uses your actual accepted-event color and text choice. Presets do not reset your palette. If the calendar begins to feel too much like an office planner, start with Minimal and leave nesting/shading off.

## Short displays and the three-day helper

**Low-height helper** offers Auto, Always on, and Off. Auto uses the game's UI display height and activates at **768 pixels or less**; UI scaling can affect the coordinate system reported by the game.

The helper starts on **yesterday, today and tomorrow**. Previous/Next moves three days at a time; Today returns to the current neighborhood. If you leave it centered on today, it follows the next day. Your normal month/week preference is preserved.

The side panel starts collapsed. **Panels / settings** reveals Pending, Settings, Resets, Appearance and Sync. **View filters** exposes the calendar visibility controls and viewed-world selector in a popup. The window has smaller minimum dimensions. You can force the helper on at larger resolutions or disable Auto.

## Detail and performance

**Invite lists** offers:

1. **Server > event > message:** groups start closed. Open a server, then an event for facts/actions, then Message for the original text. Recommended for large collections.
2. **Summaries:** titles, basic facts and short previews, in pages of 25.
3. **Full listings:** the same summaries plus original message text, in pages of 25.

Day-number clicks still open the day's event list, with expandable event and message details. The stored message is the retained canonical invitation; this is not an archive of every repost ever heard.

**Lightweight grid** replaces the old Faster calendar name. It draws fewer child windows and clips labels. The full grid allows scrolling inside week days. Both use the same event data. Nesting and overnight styles work with either grid.

**Show calendar draw timing** reports a smoothed CPU draw average and peak while this window is open. Reset the peak, use the same date range and opened details, and compare modes. This is not total game frame time or GPU time, and it does not measure all plugin background work.

The implementation caches parsed shared clocks, day layouts, overnight spans and nesting. Background display snapshots are checked at most four times per second; local saved edits request a refresh. Building the month grid no longer performs a redundant event-by-day scan just to obtain day cells. Alarm preparation and past cleanup run once per second instead of once per frame. These changes reduce repeated work; in-game FPS improvements still depend on the scene and hardware.

## Worlds and the red summary

Three controls have separate purposes:

| Control | Purpose |
| --- | --- |
| Sync > Servers | Which worlds to fetch |
| Calendar selector / compact View filters | Which worlds to draw on the grid |
| Pending from | Which worlds to include in the Pending feed |

**Pending from** appears only while Sync is attached and offers Current world, Open calendars, and All synced worlds. It does not subscribe to all game worlds. When Sync is disabled, uninstalled or not attached, Pending contains only locally collected invitations; the saved Sync scope does not filter those invitations. Local channel, hidden, past-event and acceptance filters still apply. Re-enabling Sync restores the saved scope. The former Show all servers preference migrates to Current world or Open calendars. Sync acceptance filters are also hidden in both normal and compact views while Sync is absent.

While Sync is attached, the red bar reads **Viewing calendars: Local + Sync:** and then the open worlds. It stays visible even if Pending is empty or invitations were auto-accepted. It still summarizes data centers compactly. Click it to expand counts of event occurrences in the displayed date range under the current calendar filters. Overnight continuation chips count once; separate recurring occurrences count separately. Counts use advertised destinations when present, otherwise the stored world. The bar can be hidden in Appearance.

## Hunt trains and other gatherings happening now

An invitation such as “Hunt train assembling at Urqopacha (28.2, 13.0)” supplies an implied current time and a map destination. Similar present-tense hunt/FATE/rank/map-party gathering invitations are recognized when they include a named map location and coordinates. Historical, cancelled and future wording does not qualify for the implied-now rule.

The event is timed to when the message was heard, with a **Now** label for up to 30 minutes after that observation. No end time is invented. Map actions use the parsed coordinates. A gathering without an advertised world uses the world where it was heard, rather than a visitor's home world. Shared copies carry an absolute start instant so other timezones see the correct time.

This does not force a new sound preference: existing accepted/unaccepted alarm settings continue to apply.

## Reading sync status

The top-left line reports connecting, login wait, uploading, checking the relay, applying updates, up to date, catching up, retrying, or an update requirement. **Up to date** uses muted green. The next line reads, for example, **Last success 14:32:10 · Public relay (12 seconds ago)**, using whole seconds, minutes or hours as time passes. The built-in endpoint is labeled **Public relay**; custom addresses remain visible. Hover the second line for transfer details and recovery guidance. Status text and colors, including the Sync tab status, collect rapid changes within a 100 ms display buffer to reduce flicker; transfers and manual refresh requests continue immediately. An old successful status stops looking current after 45 seconds without an update.

“Applied” includes new invitations and changed details. Zero applied is normal if nothing changed. Zero downloaded bytes may mean a cached catalog. A successful fetch does not force hidden, declined, deleted, or filtered invitations onto the calendar.

- **Sync now:** request the next pass, skipping the login wait.
- **Refresh details:** fetch again while preserving personal decisions.
- **Restore deleted shared invites:** explicitly clear shared deletion choices, including recurring exclusions; show past shared events and turn automatic past cleanup off. Hidden/declined choices remain. Recovery is limited to what the relay still retains; it cannot reconstruct local-only entries or already-expired relay data.

Deleting one occurrence does not delete the whole recurring series. Shift-click Delete removes that occurrence; Ctrl-click removes the series; ordinary click offers the scopes. If both modifiers are held, Shift takes precedence.

## Diagnostics and relay availability

Optional performance logging keeps bounded local samples and mirrors enabled samples to the Dalamud log. Samples contain counts, timing and byte totals rather than invitation bodies.

**Backup relay** defaults to **Public relay**. When primary and backup identify the same endpoint, Sync uses only one. A custom primary automatically falls back to Public relay on an outage. Choose **Custom** for another compatible relay, or **Off** to keep a private group on its primary only. Existing custom backup addresses are preserved during migration. On an availability failure the client uses the configured backup and waits 60 seconds before probing the primary again. It does not switch to evade rate limits, authentication, upgrade requirements or invalid signed data. The selected backup receives eligible Shout/Yell contributions on failover; public fallback makes those invitations available on the public relay. The backup selector replaces the old Country mirror checkbox.

This is relay redundancy, not player-to-player networking. A second endpoint is useful only if it is independently available and has replicated the relevant data. Saved invitations remain available during outages.
