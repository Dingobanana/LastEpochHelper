# Changelog

Shown inside the app after an update. One section per version: `## <version> - <title>`.

## 0.7.15 - The trees remember where you are

- Fixed: after closing and reopening the build tree, a skill or the passives could show fewer points than you had, so you had to press + a few times to get back. The points read off the game's panel are often incomplete (a tooltip in the way, small print); the tree was drawn from them anyway. Now what was read is only shown when it accounts for all the points the tree is known to have - otherwise the plan is shown at the number of points you had.
- A skill's level is now read from the game ("LEVEL 7" under the open tree's heading) and used as its points, so you rarely need the slider for skills at all.

## 0.7.14 - The Weaver tree

- New: a Weaver tab in the build tree. The Weaver tree is the same for every class and is not part of a build guide, so the overlay fetches Maxroll's ready-made Weaver trees (Starter, General Endgame, Experience and Favor, Nemesis, Rift Beast, Omen Window, Boss Farming, Weaver's Will, Exiled Mage) and shows the one you choose, with the order of its points, beside your build's own trees. Choose it in the gold box at the bottom of the Weaver tab; set how many Weaver points you have with the slider. They are fetched along with a build import, or with Settings → Character → Get the Weaver trees from Maxroll.
- A build that has its own Weaver points keeps them as the choice "From this build".
- The Weaver tab only shows for a character in the endgame (level 55 and up, in the Monolith chapter, or with the campaign guide hidden) - it has no use before that.

- The overlay cannot read what its own windows cover. If the guide box, the build tree or the planner lies over the top left of the game's passive / skill panel - where the headings it reads are - the build tree stops following, with nothing to show why. The overlay now tells you to move the window when that happens.
- A build tree opened for the first time now starts on the right, below those headings, instead of on top of them.

## 0.7.13 - Following skills with HDR on

- With HDR switched on, Windows gives programs a washed-out copy of the screen, and the small grey print the overlay used to recognise an open skill tree was lost in it - so the build tree stopped following the skill you opened. Two changes: the overlay now restores the contrast of such a picture before reading it, and it recognises an open skill tree by the skill's name with "LEVEL n" in white right under it, which stays readable.
- The activity log (in a bug report) now says how the picture of the screen came out, so a problem like this can be seen instead of guessed.

## 0.7.12 - No more jumping between skills

- Fixed: on the game's Skills & Specializations overview the build tree could jump from one skill's tab to another every second or two. The overview lists every skill at the same size, and the overlay took whichever name happened to be read slightly bigger for an open skill tree. A skill is now only followed when the game really has that skill's tree open.

## 0.7.11 - An import that takes what it is given

- More ways to bring a build in: a shared planner link ending in "#2" now opens on that version; a guide page opens on the version the guide is written around; the text Maxroll's planner copies with its Export button can be pasted straight into the link box; and "Import from file" takes a build file a friend sent you (the name.tree.json the overlay saves, with or without its name.txt) or a planner saved as JSON.
- Planners made the way people actually make them now import sensibly: leveling builds whose stages are all left at level 100 are recognised as stages by their growing trees; a gear-only version is no longer the one you start on; versions with the same name are told apart; untitled planners get a name; builds from older patches lose only the points on nodes that no longer exist.
- Whatever is pasted, the answer is a sentence: a Last Epoch Tools link, another game's planner, an overview page with many builds, a deleted or private planner, a link cut short, text that is no link at all - each says what it is and what to paste instead.
- Nodes that items push over their limit ("5/4", even "10/5") are drawn, read off the game and adjustable as planned.
- Build files with odd names (any language, characters Windows does not allow, very long names) are saved safely; a damaged or hand-edited build file no longer stops the overlay.
- Tested against 366 real planners from Maxroll - every official guide and about 300 community builds from seasons 2 to 5, all classes - and against tens of thousands of deliberately damaged copies of them.

## 0.7.10 - Out of the way, and room on small screens

- The guide box can now be shown only while the mouse rests on it (it fades to a faint outline that lets clicks through, and comes up by itself for a message), or be hidden altogether until you ask for it with the show / hide hotkey or the tray icon. Settings → Overlay → Show the box.
- The build tree fits small screens: it shrinks by itself when the screen is too small for it, Settings → Following the game has a size slider, and a new "Mini" switch at the bottom right of the tree puts the picture away and leaves just the tabs and the next points.
- Boss notes: the red boss card now says what to do or avoid for most campaign bosses, the three dungeon bosses, Majasa and all ten Monolith timeline bosses (from Maxroll's boss guides and campaign walkthrough). Still name-only, for lack of a good source: Idol of Loathing, Void Centipede, Primeval Dragon and The Observer.
- The README is now in English.

## 0.7.9 - Choose what the overlay shows

- Every part of the overlay can now be switched on or off in Settings → Overlay: zone name and steps, boss notes, unclaimed rewards, next zone, reminders, the passive / idol counters, your level, the timer and the zone map.
- Settings is split into tabs: Character, Overlay, Following the game, Hotkeys, Version. The game's map key can now be set there too.
- New in the ☰ menu: Hide the campaign guide (endgame). For a character that is in the Monolith and no longer cares which zone comes next, the zone steps, boss notes and unclaimed rewards are put away; the build reminders, the passive / idol counters and the timer stay. It is remembered per character - tick it again to bring the guide back. The overlay mentions it once when you reach the Monolith.

## 0.7.8 - Only the stage's skills

- With a guide that has stages, the build tree now shows only the skills of the stage in use - not every skill the guide ever uses. A skill the game has open stays visible even if the stage does not use it.

## 0.7.7 - Imports that understand the guide

- Most Maxroll build guides hold several versions of one build (Starter, Endgame, Aspirational, sometimes a hardcore or alternative setup). The import used to read those as steps to take one after another, which produced respecs and skill swaps that are not part of any build. Now all versions are imported side by side and one is in use - the first by default.
- "Which part of the guide am I on?" is now answered - and changed - at the bottom of the build tree: a gold box shows the stage (leveling guides) or the version (build guides) in use. Click it to pick another; for stages, "Follow my level" is the default and moves on by itself. The same choice is on Planner → TL;DR.
- Importing again no longer wipes your ticked reminders, the points you set on skills or the tab you were on.
- A class overview page (many builds on one page) is refused with a clear message instead of importing whichever build came first.

## 0.7.6 - Steadier following

- The build tree no longer jumps to another skill or tab and back when one look at the screen comes out wrong: after the first look, a change of panel, skill or passive tab has to be seen twice in a row. This matters most where the screen is hard to read, for example with HDR on.
- The passive tab is followed from its title only; the node labels no longer get a say once the title has been read (the two could disagree and take turns).
- Bug reports now include the imported build and whether the icon sheet could be opened, to track down missing icons.

## 0.7.5 - The build in short, a tour, no more blinking

- New: Planner → TL;DR. The imported build boiled down to what to do in which order: the mastery to choose and when, which passive trees get points (also when a leveling build spends points in one mastery's tree but ends in another), and which skills to specialize. Works for builds you already imported.
- New: Take a tour (the ☰ menu, and offered once after this update). A few cards that walk through the overlay and open the windows they talk about. Skip it whenever you like.
- Fixed: the build tree blinking while a skill or passive panel is open. A point value read off the game is now only taken when two looks in a row agree, the tree is only redrawn when something in it changed, and holding P or S a moment too long no longer opens and closes it.
- The "specialization slot unlocked" reminders tick themselves once the overlay has seen that many skills with points in the game. The other green reminders are plain reminders: the overlay cannot see whether you did them.

## 0.7.4 - Panels opened by mouse, reports that send themselves

- The build tree now also opens when the game's passive or skill panel is opened with the mouse (for example the "+" for unspent points), not only with the P / S keys. It checks every couple of seconds; if you close the tree yourself it stays closed until the game's panel is closed.
- Fixed: the build tree did not switch between your skills when another skill's name was on screen in letters nearly as big (the skill bar). The open tree's own heading is now what counts.
- The passive tree now follows the tab you open in the game by reading the tab's title, instead of guessing from the node labels - which failed on the mastery tabs.
- Report a bug has a Send button that delivers the report directly, with no file to pass on. If sending fails the report is saved to your desktop as before.

## 0.7.3 - Bug reports, steadier mirroring

- New: Report a bug (the ☰ menu). Describe what went wrong and the overlay saves one zip file to your desktop with your description, its error and activity logs, what it last read off the screen and its settings. Nothing is sent automatically; pass the file on to whoever shared the overlay with you. Account and character names are removed.
- Fixed: opening the tree of a skill your imported build does not use made the build tree close after a few seconds and stay closed. The overlay now recognises any open skill tree and stays up.
- Fixed: after moving the points slider, a tree kept showing the plan instead of your real points. The slider is now a preview that lasts while the tree is open; points spent in the game always show.
- Icons: each imported build now keeps the icon sheet it was imported with, so a later import can no longer shift another build's icons. Re-import a build whose icons look wrong.
- The line under the passive tree no longer judges the points read off the screen (that read can miss nodes, which made the warning wrong). It now just states your total: points from your level plus quest rewards from the map.

## 0.7.2 - Counters from the map, skill trees read properly

- The "x/15 passives" and "x/8 idol slots" counters are now taken from the game's own map: open the map (M) and the overlay reads "Passive points rewards (6/15)" and "Idol slot rewards (1/8)" from its bottom left corner. Side-quest rewards listed as unclaimed are ticked off when the numbers show you have them. The map key can be changed, or the reading turned off, in Settings.
- Fixed: opening a skill tree that had unspent points (for example Summon Thorn Totem) made the build tree jump back to the passives tab.
- Fixed: a tab you picked yourself in the build tree was taken away again a moment later. The tree now follows the game when the game changes what it shows, and otherwise leaves your choice alone.
- New self-check under the passive tree: the points seen in the game are compared with your level and counted quest rewards ("✓ 17 points = level 13 + 6 quest passives"). If they disagree it says which level the points would mean, so you can tell that something was read or counted wrong.
- The order number (blue) and the "+N points" tag (orange) on the next nodes now have their own colours and corners, with matching switches at the bottom; a little more room under the tree.
- Points in skill trees are now read from the game far more reliably: the small labels under the nodes get their own enlarged read.

## 0.7.1 - Steadier sync with the game

- A skill tree that could not be read reliably is no longer shown as having zero points; the overlay keeps the plan view for it instead.
- The "● game" switch in the build tree now says how long ago your points were read from the game.
- Reading a tree needs most of its nodes to be visible; move the mouse off the tree if a tooltip is covering it.

## 0.7.0 - Filters from your build, real tree points, item check

- Loot filter from your build: the planner's Loot filter page writes a leveling filter for the imported build straight into the game's folder.
- One filter for several builds: tick more than one imported build before generating, for a main and an alt sharing loot.
- "+ build" next to a filter makes a copy that never hides what your builds want (their uniques, items with two of their affixes, idols), on top of the filter's own rules.
- Search page: ready-made stash search strings for the build's affixes and items; click to copy.
- Weaver tree: shown as a tab in the build tree when the imported build has one.
- Prophecies page rewritten in plain words, with every lens and what it does.
- Filter check: "check" next to a filter lists uniques and affixes that are newer than anything the filter knows.
- Targets page: which timeline gives each blessing and unique the build wants, with the roll ranges.
- Item check (Ctrl+Shift+E): hover an item and the overlay says which of the build's affixes are on it.
- Click an affix on the Gear page to copy it for the stash search.
- Death journal: every death is logged with zone and level; note the cause with a click.
- Morditas and Prophecies reference pages for the 1.5 systems, and a "Before Empowered" checklist step.
- Bar layout: boss tactics and alerts now get their own full-width row instead of being cut off.
- Build tree: icons now load when the tree is opened with the game's P/S keys (they only appeared when it was opened by hotkey).
- Build tree: follows the skill you open several times a second instead of once a second.
- Build tree: the order numbers and the "+N points" tags can be switched off separately.
- Build tree: reads your real points from the game. While the passive panel (or a skill tree) is open, the counts under the game's nodes are read off the screen and shown in the overlay; "next" skips what you already have and points outside the plan are ringed red.
- Build tree: opens and closes with the game's panel by looking at the screen, so it no longer ends up inverted.
- Build tree: a slider at the bottom sets how many points the plan places (passive points, or a skill's level); click a node to add a point, right-click to remove one.
- The build's per-level text lines are now off by default (settings: "List the build's per-level steps"); the reminders for your level stay.
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
