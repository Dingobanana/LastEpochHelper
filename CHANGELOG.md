# Changelog

Shown inside the app after an update. One section per version: `## <version> - <title>`.

## 0.7.0 - Filters from your build, real tree points, item check

- Loot filter from your build: the planner's Loot filter page writes a leveling filter for the imported build straight into the game's folder.
- Filter check: "check" next to a filter lists uniques and affixes that are newer than anything the filter knows.
- Sync with your real character: "sync" in the build tree reads your passive and skill points from your public Last Epoch Tools profile. Taken points are shown as they are, "next" skips what you already have, and points outside the plan are ringed red.
- Targets page: which timeline gives each blessing and unique the build wants, with the roll ranges.
- Item check (Ctrl+Shift+E): hover an item and the overlay says which of the build's affixes are on it.
- Click an affix on the Gear page to copy it for the stash search.
- Death journal: every death is logged with zone and level; note the cause with a click.
- Morditas and Prophecies reference pages for the 1.5 systems, and a "Before Empowered" checklist step.
- Bar layout: boss tactics and alerts now get their own full-width row instead of being cut off.
- Fixed: on a fresh install, settings (such as the overlay's position) were not saved.
- Re-import your Maxroll build once so the filter generator has the data it needs.

## 0.6.0 - Planner, boss cards, bar layout

- New planner window (the ▤ button or Ctrl+Shift+G) with five pages:
  - Gear: what the imported build wears at your stage, which affixes to look for, and a tick per slot.
  - Idols: the build's idols and blessings, with the timeline each blessing comes from.
  - Loot filter: give each installed filter a level; the overlay tells you when to switch.
  - Monolith: per timeline tick normal/empowered, pick the blessing you took, keep corruption; Knowledge of Orobyss is counted for you.
  - Dungeons: entrance, boss mechanic, reward, skip exit, key sources, your key count and first clear.
- Re-import your Maxroll build once to get the gear and idol pages.
- Boss card: bosses in a zone are shown in a red box that stays visible in compact mode.
- Shield lines: which damage types to expect in each era, and resistance reminders at level 25 and 55.
- Bar layout: one line across the screen. Ctrl+Shift+C now cycles box, compact, bar.

## 0.5.0 - Updates from GitHub

- The app checks GitHub for a new version at start and every six hours; a green line in the overlay tells you when there is one. Click it (or use "Look for update" in settings) to download and install - the overlay restarts itself.
- After an update, a "What's new" window lists everything that changed since the version you had.
- Only one copy of the overlay can run at a time; starting it twice no longer gives two overlays.

## 0.4.0 - Follow the skill you open

- The build tree switches to the skill whose tree you open in the game (it reads the skill name on screen with Windows' text recognition).
- "① order" button in the build tree: turn the green next-point rings on or off, separately for passives and skills.
- Next steps now say how many points go into the node ("+2"), on the node and in the "Next" line.

## 0.3.1 - Shorter text, directions, leveling route

- Zone steps are short directives ("Talk: Grael → Talk: Keeper Leena") instead of every quest line.
- New "➜" lines with where to go and what the trick is ("You only need ONE of the two orbs").
- New route "Leveling: Chapters 1-7, then Monolith" for leaving the campaign once the quest caps are reached.

## 0.3.0 - Build tree

- A window that mirrors the passive tree and the skill trees with icons, showing which points to take next.
- Opens with the game's own keys for passives (P) and skills (S); Ctrl+Shift+T also toggles it.

## 0.2.0 - Profiles, builds, timers, maps, Monolith

- Character profiles with their own progress, route, play time and death count.
- Import a build from a Maxroll planner or guide link; per-level build lines in the overlay.
- Alternative routes that skip chapters through dungeons.
- "Unclaimed rewards" for side quests with passive points or idol slots you walked past.
- Zone map pictures you capture yourself, a compact mode, auto-hide when the game loses focus.
- Settings window, tray icon, and a Monolith checklist after the campaign.

## 0.1.0 - First version

- Step-by-step campaign overlay that follows you from zone to zone by reading the game's log file.
