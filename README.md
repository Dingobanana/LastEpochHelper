# Last Epoch Helper

A companion overlay for Last Epoch (patch 1.5 / Season 5), inspired by Exile UI's act tracker for Path of Exile.
It shows what to do in the zone you are in, which side quests give passive points or idol slots and which
you can safely skip, and what your build wants at your level - next to the game, without alt-tabbing.

The overlay is an ordinary Windows window that stays on top. It only reads the game's own log file
(`Player.log`) and what is on your screen. It never touches the game's memory or files and never sends input
to the game.

## Getting started

1. Set the game to **Borderless Windowed** (nothing can draw over exclusive fullscreen).
2. Download the latest `LastEpochHelper-x.y.z.zip` under [Releases](https://github.com/Dingobanana/LastEpochHelper/releases),
   unpack it somewhere permanent and start `LastEpochHelper.exe` (the exe carries everything it needs). An "LE" icon
   appears by the clock.
   The first time, Windows shows "Windows protected your PC": **More info** → **Run anyway**. Windows says this about
   programs that are not signed with a paid certificate and that few people have run yet. The source is public, each
   release is built from it, and your antivirus can scan the exe like any other.
3. Play. The overlay moves to the right step when you enter a new zone, and hides when the game does not have focus.

The build tree and the map counters read the game's text off the screen, so they need the game
in **English** and Windows' text recognition for English (it comes with the English language in Windows' language
settings). The overlay says so at start if either is missing, and Settings → Following the game → **Check** shows
what it can see. The campaign guide works in any language.

**GeForce NOW:** the overlay recognises the game's window ("Last Epoch on GeForce NOW"), so the build tree and the
map counters work. The game's log stays on Nvidia's machine, though, so the campaign guide cannot follow your zones
there (step through it with `Ctrl+Shift+Left/Right`), and loot filters cannot be written into the game.

New here? The menu (`☰`) has **Take a tour**, a two-minute walk through everything below.

| Hotkey | What it does |
| --- | --- |
| `Ctrl+Shift+Right` / `Ctrl+Shift+Left` | Next / previous step |
| `Ctrl+Shift+H` | Show / hide |
| `Ctrl+Shift+L` | Lock (clicks go through to the game) / unlock |
| `Ctrl+Shift+C` | Switch between box, compact box and horizontal bar |
| `Ctrl+Shift+G` | Planner: build summary, gear, idols, loot filter, Monolith, dungeons |
| `Ctrl+Shift+T` | Build tree |
| `Ctrl+Shift+M` | Zone map: off / in the overlay / large in the middle of the screen |
| `Ctrl+Shift+S` | Save a screenshot of the game as the map for the zone you are in |

Unlocked, drag the top of the box to move it and click a line to tick it off. `☰` jumps to a chapter, switches
route or character, hides the campaign guide for an endgame character, and opens the tour and the bug report.
`⚙` opens the settings. The tray icon shows or hides the overlay and can quit the program. All hotkeys can be
changed in the settings.

Line types: `▸` main quest · `◆` side quest worth doing · `☠` boss · `•` tip · `✕` can be skipped.
A yellow / purple mark means the quest gives a passive point / idol slot.

## Updates

The app asks GitHub for a new version at start-up and every six hours. If there is one, a green line appears at
the top of the overlay; click it (or use **Look for update** in the settings) to download and install. The overlay
restarts itself and a "What's new" window lists what changed since your version. Nothing is installed without you
asking, and only zip files from this repository's own releases are downloaded. Your progress lives in
`%APPDATA%\LastEpochHelper` and is not touched.

## Features

### The campaign guide

- **Follows you.** The game logs every zone change; the overlay jumps to the nearest step in that zone.
  Unknown zones (new in a patch) are learned automatically.
- **Routes.** The full campaign, a shorter leveling route, two routes that skip chapters through dungeons
  (Lightless Arbor + Soulfire Bastion, or Lightless Arbor + Temporal Sanctum), and a speedleveling route after
  [keban666's guide](https://keban666.github.io/Last-Epoch-Speedleveling-Guide/): Chapters 1-5, the Monolith,
  then the Temporal Sanctum to Chapter 9.
- **Quest rewards.** "x/15 passives · x/8 idol slots" counts your quest rewards. Open the game's map (`M`) and the
  overlay reads the real numbers from the corner of the map. Side quests with a reward that you walked past stay
  listed until you click (done) or right-click (skipped) them.
- **Automatic ticking.** The game logs the name of some quest steps; those are ticked when the name matches a line.
- **Boss notes.** Bosses in the zone get a red card with what to watch out for.
- **Monolith.** After chapter 10 comes a checklist of timelines in a recommended order, quest echoes, bosses and blessings.
- **Blessing choice.** When the game offers blessings after a timeline boss, a note beside it names the one the
  imported build takes from that timeline. The overlay knows the timeline from the game's log, so this does not
  work on GeForce NOW.
- **Zone maps.** Open the map in the game and press `Ctrl+Shift+S`; the picture is shown whenever you are in that zone.
- **Time.** Play time per character, time in the current chapter, your best time for that chapter, and deaths.
- **Choose what you see.** Every part of the box (zone and steps, boss notes, rewards, next zone, reminders, counters,
  level, timer, map) can be switched off in Settings → Overlay. The box can also show only while the mouse rests on it,
  or stay hidden until you ask for it. For a character in the endgame, `☰` → *Hide the campaign guide* puts the
  campaign away and keeps the rest.

### Your build

- **Import.** Paste a Maxroll planner or build guide link in Settings → Character and press *Import build*.
  Leveling guides come in as stages by level; build guides as their versions (Starter, Endgame, Aspirational, ...).
  A link ending in `#2` opens on that version. The text the planner's *Export* button copies can be pasted in the
  same box, and *Import from file* takes a build file someone sent you (`name.tree.json` from their
  `%APPDATA%\LastEpochHelper\builds` folder).
- **Last Epoch Tools.** A planner link from lastepochtools.com (`lastepochtools.com/planner/...`) imports too, with
  the order the points were placed in. Only the passive, skill and Weaver trees come along, not its gear or idols. The
  site's owner allowed this overlay to read its public build data. It is fetched only when you press *Import build*.
- **TL;DR.** Planner → TL;DR is the build in short: which mastery to choose and when, which passive trees get points in
  which order, which skills to specialize, and which stage or version you are on.
- **Build tree.** Press the game's own keys for passives (`P`) or skills (`S`) - or open the panel with the mouse - and
  the build's tree opens beside the game's panel, drawn as in the game, and follows the tab or skill you open.
  The line at the top says what the picture is: your character's points, read off the game's panel, or the guide's
  plan when they cannot be read (with what to check). The next nodes carry a tag like `1 · +2` - the first step puts 2
  points there. *Toggle Slider* shows a slider to see the plan at any number of points; *Back to my character* ends
  it. The gold box at the bottom chooses the stage or version of the guide. On a small screen the window shrinks to
  fit, and *Mini* leaves just the tabs and the next points. The game's keys are only listened to - they still reach the game.
  Moving with WASD? Enter your skills key under Settings → Following the game, or leave it empty. If a key does
  not open its panel three times in a row, the overlay stops following it by itself and says so.
- **Reminders.** The green lines are reminders for your level: new specialization slots, a loot filter, resistances.
  Click to tick one, right-click to tick it and all earlier ones.
- **Planner (`▤`).** *Gear* and *Idols* (what the build wears at your stage and which affixes to look for),
  *Targets* (where its uniques and blessings come from), *Search* (ready-made stash search strings, click to copy),
  *Loot filter* (a filter generated from your build, written straight into the game's folder; filters by level;
  a check of filters against the current patch), *Monolith* and *Dungeons* (checklists), *Deaths* (a journal of
  where and why), and reference pages for the 1.5 systems.
- **Character profiles.** Every character has its own progress, route, time and build. A new character gets a
  profile by itself; the log only names a character when it is created, otherwise it is recognised by class,
  mastery and level.

### When something goes wrong

`☰` → **Report a bug**: describe what happened and press Send. The report (your description, the overlay's error and
activity logs, what it last read off the screen, its settings, the end of the game's log and optionally a screenshot)
goes to the maintainers. Account and character names are removed. A build made from this source code has no address
to send to and saves the report as a zip file on your desktop instead.

Every report gets a random number such as `K7Q2-9XMB`. If we answer, the answer is published under that number in
[`replies.json`](replies.json), and for 60 days the overlay looks there every few hours: a gold line "We answered
your bug report" appears, click it to read. Only your computer knows which number is yours, so nobody - including
us - can tell who an answer is for.

### Answering a report (maintainers)

The number is on the second line of the report's message and in its `report.txt`. Add an entry to `replies.json`
on the main branch (GitHub's web editor will do) - it is public, so write nothing private in it:

```json
{ "replies": [
  { "id": "K7Q2-9XMB", "date": "2026-10-08", "text": "Thanks! Fixed in 0.7.28 - update from the green line." }
] }
```

A second answer to the same report needs another date. Old entries can be removed after 60 days.

## How it works

- The game writes `Scene Z32 load started: load mode: Single` to `Player.log` on every zone change.
  A scene list built into the exe (`src/LastEpochHelper/Data/scenes.json`) maps scene ids to zone names, and the tracker jumps to the nearest step in that zone
  (at most a few steps forward or back, so a trip to town does not send you far away).
- Everything the overlay knows about open panels, your points and tooltips comes from Windows' built-in text
  recognition run on a screenshot of the game window. It can be switched off in Settings → Following the game.
- Quest rewards are shared: the first 15 passive points and 8 idol slots from quests count, whichever quests they
  come from. The route reaches both caps in chapters 6-7; after that side quests are marked optional.
- Everything is stored in `%APPDATA%\LastEpochHelper` (`profiles.json`, `settings.json`, imported builds, logs).

## Sources and caveats

- Quest steps, rewards, zone levels and scene ids are datamined (<https://lastepoch.tunklab.com>, build `1.5-preview1`).
  The rewards agree with EHG's official support article for chapters 1-9.
- The 15 / 8 caps are not officially confirmed for 1.5. The three dungeon key caches new in 1.5 are inferred from
  the datamine; the patch notes do not name the zones.
- The alternative routes were put together from the datamined exits, and players have played them through. After a
  skip the game's quest log can still differ a little from the list - follow the in-game marker there. The
  speedleveling route follows keban666's order with steps from the same game data as the others; "Monolith from
  level 30" is keban666's advice for a strong build.
- Monolith data: timelines and blessings from tunklab, recommendations from Maxroll (dated 1.4).
- Builds, tree layouts and icons come from Maxroll's planner at import time and stay on your machine. Builds from
  Last Epoch Tools are read from its public build data (with its owner's permission) and drawn with Maxroll's trees.
- This is a fan project, not affiliated with Eleventh Hour Games or Maxroll.

## Building from source

Requires the .NET 8 SDK on Windows 10 (19041) or later.

```
dotnet test
dotnet run --project src/LastEpochHelper
```

The importer can also be run over a folder of planners saved from Maxroll - conversion with every consistency check,
random damage to the planners and to the game data, drawing every window, and live imports. These tests only run when
asked for through environment variables; see `tests/LastEpochHelper.Tests/MaxrollSurveyTests.cs`.

### Updating the guide after a patch

The route (zone order), the choice of side quests and the tips are written by hand in `tools/build_guide.py`;
the rest is fetched.

```
python tools/build_guide.py --refresh    # fetches the pages again and writes src/LastEpochHelper/Data/guide.json + scenes.json
dotnet test
```

The script prints a `WARNING` if a quest step cannot be placed on the full route. After a major or minor patch, also set
`GAME_VERSION` at the top of the script to the new version: the overlay reads the game's version from its log
and, once per version, tells players when the game is a newer major or minor version than the guide's data (smaller
patches within a version say nothing). Zones the overlay meets but does not know are listed in
`%APPDATA%\LastEpochHelper\unknown_scenes.txt`.
`tools/monolith.json` is the source of the Monolith chapter.

### Releasing a version

1. Set `<Version>` in `src/LastEpochHelper/LastEpochHelper.csproj` and add a section `## x.y.z - title` at the top of `CHANGELOG.md`.
2. Commit.
3. `pwsh tools/release.ps1` runs the tests, builds the zip and creates the GitHub release `vx.y.z` with the changelog
   section as its text. Running installations find it by themselves.

## License

The code and text in this repository are under the [MIT License](LICENSE): use, change and share them as you like,
keeping the copyright notice.

That covers this project's own work only. Last Epoch, its name, artwork, icons and game data belong to Eleventh Hour
Games; build planners and their icons belong to Maxroll, and Last Epoch Tools builds to that site; the datamined quest and zone data comes from
<https://lastepoch.tunklab.com>. None of that is licensed here.
