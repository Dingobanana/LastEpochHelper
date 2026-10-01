#!/usr/bin/env python3
"""Builds Data/guide.json and Data/scenes.json for the overlay.

Quest objectives, quest rewards, zone levels, waypoints and the internal scene ids come from
https://lastepoch.tunklab.com (datamined from the game). The *route* - the order zones are visited
in, which side quests are worth doing, and the tips - is authored by hand below.

Run again after a patch:  python tools/build_guide.py [--refresh]
Pages are cached in tools/.cache so normal runs do not hit the site.
"""
import html
import json
import re
import sys
import time
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CACHE = Path(__file__).resolve().parent / ".cache"
OUT = ROOT / "src" / "LastEpochHelper" / "Data"
SITE = "https://lastepoch.tunklab.com"
GAME_VERSION = "1.5"

# Quest rewards stop once a character has this many from quests, whichever quests they came from.
PASSIVE_CAP = 15
IDOL_CAP = 8

# ---------------------------------------------------------------------------------------------
# Route: zone visits in play order. "Zone@key" gives a visit its own scene key when two different
# scenes share a display name.
# ---------------------------------------------------------------------------------------------
ROUTE = [
    (1, "Divine Era", [
        "The Old Road", "The Burning Forest", "The Keepers' Camp", "The Fortress Gardens",
        "The Fortress Walls", "The Storerooms", "The Fortress Walls", "The Keepers' Vault",
        "The Northern Road", "The Keepers' Camp", "Ulatri Highlands", "The Osprix Warcamp",
        "The Summit", "The Keepers' Camp",
    ]),
    (2, "Ruined Era", [
        "The Crumbling Ruins", "Last Refuge Outskirts", "The Council Chambers", "The Last Archive",
        "Erza's Library", "Pannion's Study", "The Council Chambers", "The Precipice",
        "The Ancient Cavern", "The Upper District", "The Armory", "The Lower District",
        "The End of Time",
    ]),
    (3, "Ruined Era", [
        "The Council Chambers", "The Sheltered Wood", "The Surface", "The Forsaken Trail",
        "Cultist Camp", "The Ruins of Welryn", "Welryn Docks", "Welryn Undercity", "Cultist Camp",
        "The Ritual Site", "The Shattered Valley", "The Abandoned Tunnel", "The Lost Refuge",
        "The Ancient Forest", "The Council Chambers", "The Courtyard", "The Temple of Eterra",
        "The Lotus Halls", "The Sanctum Bastille", "The End of Ruin",
    ]),
    (4, "Imperial Era", [
        "The End of Time", "The Outcast Camp", "Welryn Outskirts", "Imperial Welryn",
        "The Soul Wardens' Road", "The Risen Lake", "The Corrupted Lake", "The Risen Lake",
        "The Fallen Tower", "Imperial Thetima", "The Darkling Pier", "The Imperial Dreadnought",
        "The Dreadnought's Deck",
    ]),
    (5, "Imperial Era", [
        "The Shining Cove", "The Majasan Desert", "The Wraith Dunes", "Maj'elka",
        "The Sapphire Quarter", "Maj'elka", "The Oracle's Abode", "The Shining Cove",
        "The Ruined Coast", "The Oracle's Abode", "The Maj'elkan Catacombs", "Titan's Canyon",
        "The Maj'elka Waystation",
    ]),
    (6, "Imperial Era", [
        "The Desert Waystation", "The Rust Lands", "The Lower Sewers", "The Barren Aqueduct",
        "Necropolis of the Deep", "Yulia's Haven", "The Upper Necropolis", "The Citadel Sewers",
        "The Immortal Summit", "The Immortal Citadel",
    ]),
    (7, "Divine Era", [
        "The Gates of Solarum", "The Burning Forest@The Burning Forest (Ch7)", "The Scorched Grove",
        "Heoborea", "The Heoborean Forest", "The Nomad Camp", "The Wengari Fortress",
        "The Ice Caverns", "The Tundra", "Heoborea", "The Tundra", "The Temple of Heorot",
        "Farwood", "The Frozen Roots", "The Tomb of Morditas", "Heoborea",
    ]),
    (8, "Divine Era", [
        "The Solemn Path", "Smoldering Grove", "The Northern Stream", "Deep Harbor",
        "The Burning Pier", "Deep Harbor", "Lake Liath", "Liath's Road", "Thetima", "Lagon's Isle",
        "Moonlit Shrine", "The Temple of Lagon", "The Temple Depths", "Sanctum of the Architect",
        "Seafloor Colosseum", "Thetima",
    ]),
    (9, "Divine Era", [
        "Soreth'ka", "The Crossroads", "Soreth'ka", "The Dry River", "The Radiant Dunes",
        "Maj'elka Upper District", "Maj'elka Lower District", "Maj'elka Slums",
        "Maj'elka Upper District", "The Oasis", "The Crystal Mines", "The Aerie", "Majasan Heights",
        "The Temple Rooftops", "The Upper Temple", "The Lower Temple", "The Chamber of Vessels",
    ]),
    (10, "Ancient Era", [
        "The End of Time", "The Ancient Oasis", "The Crystal Cave", "The Fungal Tunnels",
        "The Lush Grove", "The Mountain Ascent", "The Draal Hive", "Draal Queen's Lair",
        "The Silent Coast", "The Garden", "The Untamed Forest", "The Dragon Grotto", "The Garden",
        "The Sky Chains", "The Temple in the Sky", "Eterra's Arboretum",
        "The Halls of Preservation", "The Observer's Prison",
    ]),
]

# Scene ids for visits that use an explicit "@key" above.
SCENE_KEY_OVERRIDES = {"F20": "The Burning Forest (Ch7)"}

# Scenes the site's zone table leaves out (dungeons): scene id -> (name, level, waypoint).
EXTRA_ZONES = {
    "Dun2Q10": ("Lightless Arbor", 22, True), "Dun2Q20": ("Titan's Hollow", 22, False), "Dun2Q30": ("Titan's Rest", 22, False),
    "Dun3Q10": ("Soulfire Bastion", 37, True), "Dun3Q20": ("Soulfire Keep", 37, False), "Dun3Q30": ("The Soul Furnace", 37, False),
    "Dun1Q10": ("The Temporal Sanctum", 55, True), "Dun1Q20": ("The Sanctum Cloisters", 55, False), "Dun1Q30": ("The Sanctum Archive", 55, False),
    "M_Rest": ("Traveler's Rest", 0, True),
}
DUNGEON_QUESTS = {"Lightless Arbor": "Titan's Rest", "Soulfire Bastion": "The Soul Furnace", "Temporal Sanctum": "The Sanctum Archive"}


def chapter(number):
    return next(list(c[2]) for c in ROUTE if c[0] == number)


def until(zones, zone, occurrence=1):
    """The start of a chapter up to and including the given visit of a zone."""
    seen = 0
    for i, z in enumerate(zones):
        seen += z.partition("@")[0] == zone
        if seen == occurrence:
            return zones[:i + 1]
    raise ValueError(zone)


def since(zones, zone):
    return zones[[z.partition("@")[0] for z in zones].index(zone):]


ARBOR_SKIP = until(chapter(3), "The Forsaken Trail") + [
    "The Surface", "The Shrouded Ridge", "Lightless Arbor", "Titan's Hollow", "Titan's Rest"]


def with_chapters(replacements, drop=()):
    return [(n, era, replacements.get(n, zones)) for n, era, zones in ROUTE if n not in drop]


ROUTES = [
    {"id": "full", "name": "Full campaign",
     "description": "Every chapter. Best for the first character of a season.",
     "route": ROUTE},
    {"id": "leveling", "name": "Leveling: Chapters 1-7, then Monolith",
     "description": "The least campaign you need: both quest caps (15 passive points, 8 idol slots) are reached by the end of Chapter 7, and nothing in Chapters 8-10 is required for the Monolith. Chapter 9 is where the item factions are - go there when you want one.",
     "route": with_chapters({}, drop=(8, 9, 10))},
    {"id": "soulfire", "name": "Alt: Lightless Arbor + Soulfire Bastion",
     "description": "Skips the rest of Chapter 3 and Chapters 5-6 through the dungeons' alternate exits.",
     "route": with_chapters({
         3: ARBOR_SKIP,
         4: until(chapter(4), "The Risen Lake", 2) + ["The Ruins of Etendell", "The Felled Wood", "Soulfire Bastion", "Soulfire Keep", "The Soul Furnace"],
         7: ["Kolheim Pass"] + since(chapter(7), "The Wengari Fortress"),
     }, drop=(5, 6))},
    {"id": "sanctum", "name": "Alt: Lightless Arbor + Temporal Sanctum",
     "description": "Skips the rest of Chapter 3 and Chapters 6-8 through the dungeons' alternate exits.",
     "route": with_chapters({
         3: ARBOR_SKIP,
         5: until(chapter(5), "The Maj'elkan Catacombs") + ["The Ruined Coast", "The Temporal Sanctum", "The Sanctum Cloisters", "The Sanctum Archive"],
         9: since(chapter(9), "The Radiant Dunes"),
     }, drop=(6, 7, 8))},
]

# The site tags a few objectives with the zone they are *given* in rather than where they happen.
OBJECTIVE_ZONE = {
    ("An Ancient Hunt", "Explore the Ancient Forest"): "The Ancient Forest",
    ("An Ancient Hunt", "You heard a piercing roar nearby. Investigate!"): "The Ancient Forest",
    ("An Ancient Hunt", "Slay the dragon!"): "The Ancient Forest",
    ("The Ruined Temple", "Form the Epoch and enter the Time Rift"): "The End of Ruin",
    ("The Immortal Citadel", "Speak with Yulia"): "The Immortal Citadel",
    ("The Immortal Citadel", "Defeat Harton, Zerrick, and Yulia!"): "The Immortal Citadel",
    ("The Immortal Citadel", "Survive the Immortal Emperor!"): "The Immortal Citadel",
    ("A Heoborean Cure", "Acquire Wengari bile"): "The Wengari Fortress",
    ("A Heoborean Cure", "Acquire an Eber liver"): "The Tundra",
    ("A Heoborean Cure", "Acquire a Bitterwing fang"): "The Ice Caverns",
    ("Apophis and Majasa", "Search for Apophis and Majasa"): "The Upper Temple",
    ("The Desert Waystation", "Find the Maj'elka Waystation"): "Titan's Canyon",
    # Duplicates of "The Symbol of Hope" / "The Last Imperial"; an empty zone drops the objective.
    ("Sanity in the Darkness", "Acquire the Symbol of Hope"): "",
    ("Sanity in the Darkness", "Acquire the Phylactery"): "",
    ("The Power of Mastery", "Walk upstairs and speak with Elder Gaspar"): "The End of Time",
    ("Saving Last Refuge", "Reach the Lower District located beyond the Armoury"): "The Armory",
}

# Covered by their parent quest / not part of a first playthrough route.
IGNORED_QUESTS = {
    "A Heoborean Cure: Bitterwing Fang", "A Heoborean Cure: Wengari Bile", "A Heoborean Cure: Eber Liver",
    "Circle of Fortune: Initiation", "Merchant's Guild: Initiation", "The Monolith of Fate",
}
# Reward side quests that cost far more time than the others; the cap is reached without them.
SLOW_QUESTS = {"A Study in Time"}
# Side quests without passive/idol rewards that are still worth doing.
KEEP_QUESTS = {"Merchants and Fortune Tellers"}

# Hand-written notes: (chapter, zone, visit number within the chapter) -> [(type, text)].
TIPS = {
    (1, "The Keepers' Camp", 1): [("tip", "Buy a weapon / gear upgrade at the vendor if you have gold")],
    (1, "The Fortress Walls", 1): [("tip", "The Storerooms entrance is off the main path - do it before the Vault")],
    (1, "The Summit", 1): [("boss", "Haruspex Orian - step out of the ground markers")],
    (2, "The Council Chambers", 1): [("tip", "Hub: vendor, gambler and respec NPC are here")],
    (2, "Pannion's Study", 1): [("tip", "Waypoint back to The Council Chambers to turn in, then return here")],
    (2, "The Precipice", 1): [("boss", "Idol of Loathing, then take the Time Rift")],
    (2, "The Armory", 1): [("boss", "Voidfused Forge - avoid the lava pools under the boss")],
    (2, "The Lower District", 1): [("boss", "Husk of Elder Pannion - stay out of the lightning bubble and fire beam")],
    (2, "The End of Time", 1): [("tip", "Pick your Mastery here - the choice is permanent")],
    (3, "The Surface", 1): [("tip", "Dungeon entrance (Lightless Arbor) is north in The Shrouded Ridge - optional")],
    (3, "The Forsaken Trail", 1): [("tip", "Spriggan Tender's Cache (one-shot) holds a Lightless Arbor Key - new in 1.5")],
    (3, "The Shrouded Ridge", 1): [("tip", "Lightless Arbor entrance - needs a Lightless Arbor Key (free one in the Forsaken Trail cache)")],
    (3, "Titan's Rest", 1): [
        ("boss", "The Mountain Beneath"),
        ("tip", "Leave through the ALTERNATE exit behind the boss to skip ahead (datamine: leads to The End of Time)"),
    ],
    (4, "The Ruins of Etendell", 1): [("tip", "Excavator Thrall's Cache holds a Soulfire Bastion Key - new in 1.5")],
    (4, "The Felled Wood", 1): [("tip", "Soulfire Bastion entrance - needs a Soulfire Bastion Key")],
    (4, "The Soul Furnace", 1): [
        ("boss", "Fire Lich Cremorus"),
        ("tip", "Leave through the ALTERNATE exit - it leads to Kolheim Pass (Chapter 7)"),
    ],
    (5, "The Ruined Coast", 2): [("tip", "Temporal Sanctum entrance - needs a Temporal Sanctum Key (from the Catacombs cache)")],
    (5, "The Sanctum Archive", 1): [
        ("boss", "Chronomancer Julra - swap eras to dodge her big attacks"),
        ("tip", "Leave through the ALTERNATE exit - it leads to The Radiant Dunes (Chapter 9)"),
    ],
    (7, "Heoborea", 3): [("tip", "Quest caps reached (15 passives / 8 idol slots) if you did the marked side quests - you can leave for the Monolith via The End of Time now")],
    (7, "Kolheim Pass", 1): [("tip", "After a skip the quest log may differ from this list - follow the in-game marker")],
    (9, "The Radiant Dunes", 1): [("tip", "Arriving from the Temporal Sanctum? Quest steps may differ - follow the in-game marker")],
    (3, "Welryn Docks", 1): [("boss", "Void Centipede spawns when you approach the chest")],
    (3, "The Ritual Site", 1): [("boss", "Void Amalgamation - kill the summoned adds first")],
    (3, "The Ancient Forest", 1): [("boss", "Primeval Dragon in the far corner of the zone")],
    (3, "The Lotus Halls", 1): [("tip", "You only need ONE of the two orbs")],
    (3, "The End of Ruin", 1): [("boss", "Emperor's Remains - kill the Omen eyes when they spawn")],
    (4, "The Outcast Camp", 1): [("tip", "Crafting (the Forge) unlocks here")],
    (4, "The Risen Lake", 1): [
        ("tip", "Take the Time Rift north to The Corrupted Lake before moving on"),
        ("tip", "Side zone The Ruins of Etendell: Excavator Thrall's Cache holds a Soulfire Bastion Key - new in 1.5"),
    ],
    (4, "The Corrupted Lake", 1): [("boss", "Prophet of Ruin + Idol of Ruin - leave the big purple circle")],
    (4, "The Dreadnought's Deck", 1): [("boss", "Admiral Harton - stay close, he casts lightning at range")],
    (5, "The Maj'elkan Catacombs", 1): [("tip", "Sapphire Nagasa's Cache (one-shot) holds a Temporal Sanctum Key - new in 1.5")],
    (5, "Titan's Canyon", 1): [("boss", "Spymaster Zerrick - only takes damage while exposed; avoid poison pools")],
    (5, "The Oracle's Abode", 1): [("tip", "Waypoint to The Shining Cove for The Sapphire Tablet, then come back")],
    (6, "The Citadel Sewers", 1): [("tip", "The 3 Imperial Watchers patrol in a circle - walk against them")],
    (6, "The Immortal Citadel", 1): [("boss", "Kill order: Yulia, then Harton, then Zerrick. The arena shrinks")],
    (7, "The Wengari Fortress", 1): [("boss", "Patriarch + Matriarch - kill the Patriarch first")],
    (7, "The Temple of Heorot", 1): [("boss", "Spreading Frost - circle around it, avoid the cold beam")],
    (7, "The Tomb of Morditas", 1): [("boss", "Frostroot Warden - watch the root slam when you are in melee")],
    (8, "Moonlit Shrine", 1): [("tip", "If the pool needs moon fragments: west = The Strand of Storms, east = The Coral Pools")],
    (8, "Sanctum of the Architect", 1): [("boss", "Architect Liath - kill the summon first, dodge ground lightning")],
    (8, "Seafloor Colosseum", 1): [("boss", "Lagon - watch for the beam sweeps and tentacle slams")],
    (9, "Maj'elka Lower District", 1): [("tip", "Scalebane Bodyguard: refuse to pay and fight him (paying costs up to 100,000 gold)")],
    (9, "Maj'elka Upper District", 1): [("tip", "Item factions unlock here: Circle of Fortune (find loot) or Merchant's Guild (trade)")],
    (10, "The End of Time", 1): [("tip", "The Monolith of Fate (endgame) is open from here; Chapter 10 can be done now or later")],
    (10, "The Observer's Prison", 1): [("boss", "The Observer - final campaign boss")],
}


# Which damage to expect, by era/chapter: (chapter, zone, visit) -> text. Shown as a shield line.
# Broad strokes from the enemy types of each era, not per-monster data.
RESIST = {
    (2, "The Crumbling Ruins", 1): "Ruined Era: almost everything deals void damage - void resistance is the one to pick up",
    (3, "The Sanctum Bastille", 1): "Emperor's Remains next: void damage",
    (4, "The Outcast Camp", 1): "Imperial Era: undead deal necrotic and poison damage",
    (6, "Yulia's Haven", 1): "Citadel bosses: necrotic, lightning and poison - bring some of each",
    (7, "The Gates of Solarum", 1): "Rahyeh's forces: fire and lightning damage",
    (7, "Heoborea", 1): "The north: cold and physical damage",
    (8, "Lagon's Isle", 1): "Lagon and his temple: lightning and cold damage",
    (9, "Soreth'ka", 1): "Desert and Nagasa: poison and lightning damage",
    (7, "Heoborea", 3): "Before the Monolith: aim for 75% in every resistance and 100% critical strike avoidance",
}

# The three dungeons: static facts for the planner window. Levels and exits are from the 1.5
# datamine; the key caches are inferred from it (the patch notes do not name the zones).
DUNGEONS = [
    {"name": "Lightless Arbor", "entrance": "The Shrouded Ridge (north of The Surface, Chapter 3)", "level": 22,
     "boss": "The Mountain Beneath", "mechanic": "Carry light: stay near fire or the darkness kills you",
     "reward": "Vaults of Uncertain Fate - spend gold for chests with chosen modifiers",
     "firstClear": "+2 passive points and +1 idol slot (counts towards the quest caps)",
     "skip": "Alternate exit behind the boss leads to The End of Time (datamine; older guides say The Risen Lake)",
     "keys": "Spriggan Tender's Cache in The Forsaken Trail (one-shot, new in 1.5); Monolith timeline bosses"},
    {"name": "Soulfire Bastion", "entrance": "The Felled Wood (off The Risen Lake, Chapter 4)", "level": 37,
     "boss": "Fire Lich Cremorus", "mechanic": "Swap your shield between fire and necrotic to match incoming damage",
     "reward": "Soul Gambler - spend Soul Embers from kills on items",
     "firstClear": "+2 passive points and +1 idol slot (counts towards the quest caps)",
     "skip": "Alternate exit leads to Kolheim Pass (Chapter 7)",
     "keys": "Excavator Thrall's Cache in The Ruins of Etendell (one-shot, new in 1.5); Monolith timeline bosses"},
    {"name": "Temporal Sanctum", "entrance": "The Ruined Coast (Time Rift in The Shining Cove, Chapter 5)", "level": 55,
     "boss": "Chronomancer Julra", "mechanic": "Shift between the two eras to get past obstacles and dodge her big attacks",
     "reward": "Eternity Cache - seal a unique with Legendary Potential and an exalted item into a Legendary",
     "firstClear": "+2 passive points and +1 idol slot (counts towards the quest caps)",
     "skip": "Alternate exit leads to The Radiant Dunes (Chapter 9)",
     "keys": "Sapphire Nagasa's Cache in The Maj'elkan Catacombs (one-shot, new in 1.5); Monolith timeline bosses"},
]

# Reference pages for the planner window. Lines starting with "!" are highlighted.
# Sources: Maxroll's copy of the 1.5 patch notes and its Rage of Morditas page.
REFERENCE = [
    {"title": "Morditas", "lines": [
        "!Rage of Morditas: touch a Blood Crystal (campaign from Chapter 2, and echoes) to start a timed kill phase.",
        "Kills extend the timer - rarer enemies extend it more.",
        "!Bloodrage charges at 20 / 50 / 90 / 140 / 200 kills. Past 200 the timer drains fast: finish there.",
        "Each charge gives a random Bloodrage buff for the encounter.",
        "Phase two: when the timer ends, everything you killed revives inside an arena of blood ice. Leaving it for 4 seconds ends the encounter.",
        "!Rewards: five slots (four equipment, one other). Each charge claims one item or performs a ritual.",
        "Frozen Ritual: sacrifice a slot, the rest reroll as that same item type.",
        "Bloody Ritual: banish a slot, the rest reroll and that item type cannot appear.",
        "Charges do not carry over to the next encounter.",
        "!Boss chain: Rage of Morditas encounters in the Monolith fill the Rage meter (campaign ones do not count).",
        "Full meter: a Morditas echo chain ending in the Circle of Frozen Blood (standard Morditas).",
        "Killing him gives Demigod's Ascendance - Pinnacle Morditas - which needs 400 corruption.",
    ]},
    {"title": "Prophecies", "lines": [
        "!How it works in 1.5",
        "You no longer roll and reroll prophecies. At a telescope you pick the kind of reward you want for a slot, and that is it.",
        "The slot then fills up by itself as you earn Favor. Each time it is full it stores one charge (up to 99).",
        "Do the slot's task while it holds a charge and the reward drops. Several charges: it drops that many times at once.",
        "!The four slots and what triggers them",
        "Mesembria (rank 1): kill a Rift Beast or use a Shrine.",
        "Dysis (rank 3): kill a Nemesis or open a Lost Cache.",
        "Eos (rank 6): kill an Exiled Mage or a Timeline Boss.",
        "Arctis (rank 9): kill a Fateweaver or an Omen.",
        "!Lenses - a modifier you put on a slot",
        "Faster: Celerity (rank 5) charges this slot faster. Charity (rank 7) charges your OTHER slots faster. Tyranny (rank 3) doubles the charge rate but eats your existing Favor.",
        "More loot: Duplication (rank 5) can duplicate the reward. Celestial Scales (rank 7) can double it, with a small chance of getting a single Grole Egg instead.",
        "Easier trigger: Acceleration (rank 6) also counts rare enemies as a target.",
        "Better rares and exalteds: Origin (rank 4) higher prefix tiers, Finality (rank 4) higher suffix tiers, Anomaly (rank 6) evens out which affixes roll, Prowess (rank 6) more skill-level affixes on helmet, body and relic.",
        "Better uniques: Curiosity (rank 8) evens out which uniques and sets drop (rare ones more often). Quality (rank 9) more Legendary Potential on uniques, and a chance to raise an affix tier on rares and exalteds.",
        "!Good to know",
        "Guides from before 1.5 describe the old reroll system and no longer apply.",
        "Before 1.5, Weaver idol prophecies needed rank 9-10 without the game saying so; whether that still holds is unconfirmed.",
    ]},
]

# Where to go / what the trick is, in a few words: (chapter, zone, visit) -> text.
# From Maxroll's campaign walkthrough (written for Season 2); zone layouts have not changed since.
GO = {
    (1, "The Fortress Walls", 1): "Kill the miniboss, then north-west",
    (1, "Ulatri Highlands", 1): "North-east to The Osprix Warcamp",
    (1, "The Osprix Warcamp", 1): "North to The Summit",
    (2, "Erza's Library", 1): "Chest at the north end",
    (2, "The Upper District", 1): "Cross the bridge, kill the pack by the statue",
    (3, "The Sheltered Wood", 1): "Northernmost path to The Surface",
    (3, "The Forsaken Trail", 1): "North-east to Cultist Camp",
    (3, "The Ruins of Welryn", 1): "South-west to Welryn Undercity (Docks is a side exit)",
    (3, "Welryn Undercity", 1): "Destroy all 3 Soul Repositories",
    (3, "The Shattered Valley", 1): "North: The Abandoned Tunnel. Time Rift: The Ancient Forest",
    (3, "The Abandoned Tunnel", 1): "S-shaped path north-east to The Lost Refuge",
    (3, "The Ancient Forest", 1): "Dragon is in the top-right corner",
    (3, "The Courtyard", 1): "North-east to the Temple Guardian",
    (3, "The Temple of Eterra", 1): "North: kill 4 voidwing nests + the miniboss to drop the purple wall",
    (3, "The Sanctum Bastille", 1): "North, then east to the Time Rift",
    (4, "The End of Time", 1): "Stairs on the left, Gaspar upstairs, then the Time Rift",
    (4, "The Outcast Camp", 1): "West to Welryn Outskirts",
    (4, "Welryn Outskirts", 1): "West. Skip the optional boss (Flame Guard Sulla)",
    (4, "Imperial Welryn", 1): "North-west. Skip the optional boss (Siege Captain Caliga)",
    (4, "The Soul Wardens' Road", 1): "West to The Risen Lake",
    (4, "The Risen Lake", 2): "West to The Fallen Tower",
    (4, "The Fallen Tower", 1): "North to Imperial Thetima",
    (4, "Imperial Thetima", 1): "South-west to The Darkling Pier",
    (4, "The Imperial Dreadnought", 1): "East to the Deck",
    (4, "The Dreadnought's Deck", 1): "North to the edge, then east",
    (5, "The Majasan Desert", 1): "Rouj Zabat gives Hidden Gems. North-east to The Wraith Dunes",
    (5, "The Wraith Dunes", 1): "East to Maj'elka",
    (5, "Maj'elka", 1): "Sigils: south path, then east, then north. Talk to Alric between each",
    (5, "The Sapphire Quarter", 1): "North; miniboss at the end, portal back to Maj'elka",
    (5, "Maj'elka", 2): "East: remove the rune to The Oracle's Abode",
    (5, "The Shining Cove", 2): "North-west then south; bridge on the right leads to the Time Rift",
    (5, "The Maj'elkan Catacombs", 1): "V-shape: south-east, then north-east",
    (5, "Titan's Canyon", 1): "East to the boss",
    (6, "The Rust Lands", 1): "East to The Lower Sewers",
    (6, "The Lower Sewers", 1): "North. Skip the Time Rift",
    (6, "The Barren Aqueduct", 1): "North to the Necropolis",
    (6, "Necropolis of the Deep", 1): "South-east to Yulia's Haven",
    (6, "Yulia's Haven", 1): "East to The Upper Necropolis",
    (6, "The Upper Necropolis", 1): "North to The Citadel Sewers",
    (6, "The Immortal Summit", 1): "East to the bridge, pull the lever, wait, then north",
    (7, "The Burning Forest", 1): "West to The Scorched Grove",
    (7, "Heoborea", 1): "South-west to The Heoborean Forest",
    (7, "The Heoborean Forest", 1): "West to The Nomad Camp",
    (7, "The Nomad Camp", 1): "West to The Wengari Fortress",
    (7, "The Wengari Fortress", 1): "Brute north-west, Beastmaster north-east (free the nomads), chieftains north",
    (7, "The Ice Caverns", 1): "Left room: Rime Giant. Then north-east to The Tundra",
    (7, "The Tundra", 1): "Kill Eber (mammoth), then portal to Heoborea",
    (7, "The Tundra", 2): "North: use the Cliff Edge to raise the ice bridge",
    (7, "The Temple of Heorot", 1): "North to the boss, then the portal to Farwood",
    (7, "Farwood", 1): "North-east to The Frozen Roots",
    (7, "The Frozen Roots", 1): "North to The Tomb of Morditas",
    (8, "The Solemn Path", 1): "East",
    (8, "The Northern Stream", 1): "South to Deep Harbor",
    (8, "Deep Harbor", 1): "East to The Burning Pier",
    (8, "The Burning Pier", 1): "Commander north-east, Saboteur south-west - you need both",
    (8, "Lake Liath", 1): "North-west to Liath's Road",
    (8, "Liath's Road", 1): "West until blocked, clear it, then north to Thetima",
    (8, "Thetima", 1): "North-west to Lagon's Isle",
    (8, "Lagon's Isle", 1): "North-west to Moonlit Shrine",
    (8, "The Temple of Lagon", 1): "Maze: south until blocked, then north-east",
    (8, "The Temple Depths", 1): "Maze: north-east",
    (8, "Sanctum of the Architect", 1): "East",
    (9, "Soreth'ka", 1): "North to The Crossroads",
    (9, "The Crossroads", 1): "North, fight, then portal back to Soreth'ka",
    (9, "Soreth'ka", 2): "East to The Dry River",
    (9, "The Dry River", 1): "East",
    (9, "The Radiant Dunes", 1): "South-east: Swarmkeeper. Then east",
    (9, "Maj'elka Upper District", 1): "North-east; right-hand entrance to the Lower District",
    (9, "Maj'elka Lower District", 1): "South-east",
    (9, "Maj'elka Slums", 1): "Hideout is farthest east",
    (9, "The Oasis", 1): "North to The Crystal Mines",
    (9, "The Crystal Mines", 1): "North-east",
    (9, "The Upper Temple", 1): "North-east: Diamond Matron unlocks the way down",
}


# ---------------------------------------------------------------------------------------------
# Scraping
# ---------------------------------------------------------------------------------------------
def fetch(path, refresh=False):
    CACHE.mkdir(exist_ok=True)
    name = re.sub(r"[^a-z0-9_]+", "_", path.strip("/").lower()) or "index"
    file = CACHE / f"{name}.html"
    if file.exists() and not refresh:
        return file.read_text(encoding="utf-8")
    request = urllib.request.Request(SITE + path, headers={"User-Agent": "Mozilla/5.0 (LastEpochHelper guide builder)"})
    data = urllib.request.urlopen(request, timeout=30).read().decode("utf-8")
    file.write_text(data, encoding="utf-8")
    time.sleep(0.15)
    return data


def clean(fragment):
    fragment = re.sub(r"<svg.*?</svg>", "", fragment, flags=re.S)
    return re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", "", fragment))).strip()


def table_rows(page):
    for key, row in re.findall(r'<tr class="ant-table-row[^"]*" data-row-key="([^"]+)">(.*?)</tr>', page, re.S):
        yield key, row, re.findall(r"<td[^>]*>(.*?)</td>", row, re.S)


def load_zones(refresh):
    zones = []
    for scene, _, cells in table_rows(fetch("/zones", refresh)):
        level = clean(cells[2])
        zones.append({
            "scene": scene,
            "name": clean(cells[0]),
            "era": clean(cells[1]),
            "level": int(level) if level.isdigit() else 0,
            "waypoint": "check-circle" in cells[3],
            "cache": "check-circle" in cells[4],
        })
    return zones


def load_quests(refresh):
    quests = []
    for _, row, cells in table_rows(fetch("/quests", refresh)):
        slug = re.search(r'href="/quest/([^"]+)"', row).group(1)
        page = fetch(f"/quest/{slug}", refresh)
        start = re.search(r"starts in (.*?)</div>", page, re.S)
        rewards = re.search(r"<dl.*?</dl>", page, re.S)
        steps = []
        body = re.search(r"<h2[^>]*>Steps</h2><ol[^>]*>(.*)</ol></main>", page, re.S)
        if body:
            for number, step in enumerate(re.split(r'<li[^>]*><span[^>]*aria-hidden="true"[^>]*>\d+</span>', body.group(1))[1:]):
                objectives = re.search(r"<ul[^>]*>(.*?)</ul>", step, re.S)
                for item in re.findall(r"<li[^>]*>(.*?)</li>", objectives.group(1), re.S) if objectives else []:
                    zone = re.search(r'href="/zone\?z=[^"]+">(.*?)</a>', item)
                    steps.append({
                        "step": number,
                        "text": clean(item.split("<span")[0]).rstrip(". "),
                        "zone": html.unescape(zone.group(1)) if zone else None,
                    })
        quests.append({
            "name": clean(cells[0]),
            "chapter": int(clean(cells[1])),
            "main": clean(cells[2]) == "Main",
            "passive": int(clean(cells[4]) or 0),
            # The site only says "Unlocked idol slot"; every such quest unlocks one.
            "idol": 1 if rewards and "idol slot" in clean(rewards.group(0)).lower() else 0,
            "start": clean(start.group(1)) if start else None,
            "objectives": steps,
        })
    return quests


# ---------------------------------------------------------------------------------------------
# Guide assembly
# ---------------------------------------------------------------------------------------------
TRAVEL = re.compile(r"^(Enter|Travel to|Reach|Find|Head|Take the portal to|Use the Time Rift to travel to) ", re.I)


def is_travel_only(text, zone):
    """'Enter the Fortress Walls' on the Fortress Walls step says nothing the zone title doesn't."""
    core = re.sub(r"^the ", "", zone, flags=re.I).lower().rstrip("s")
    return bool(TRAVEL.match(text)) and core in text.lower() and len(text) < len(zone) + 22


def normalise(name):
    return re.sub(r"^the ", "", name.lower().replace("'", "").replace("\u2019", "")).strip()


def shorten(text, zone_names):
    """'Speak with Keeper Leena in the Keeper's Camp.' -> 'Talk: Keeper Leena'."""
    t = text.strip().rstrip("!. ")
    # A trailing "in/at <zone>" repeats what the step title already says.
    tail = re.search(r"\s+(?:in|at|within|of) (?:the )?([A-Z][\w' \u2019-]+)$", t)
    if tail:
        place = normalise(tail.group(1))
        if any(place == z or place.rstrip("s") == z.rstrip("s") or place.replace("keepers", "keeper") == z.replace("keepers", "keeper") for z in zone_names):
            t = t[:tail.start()]
    t = re.sub(r"^(?:Speak|Talk) (?:with|to) (?:the )?", "Talk: ", t)
    t = re.sub(r"^Meet (?:with )?(?:the )?", "Talk: ", t)
    t = re.sub(r"^(?:Defeat|Slay|Destroy|Kill) (?:the )?", "Kill ", t)
    t = re.sub(r"^Return to (?:the )?", "Back to ", t)
    t = re.sub(r"^(?:Acquire|Retrieve|Take|Pick up) (?:the |an? )?", "Get ", t)
    t = re.sub(r" once again$| again$", " (again)", t)
    return t[:1].upper() + t[1:]


def compress(tasks, zone_names, per_line=4):
    """Merges a zone's objectives into short one-line directives; reward lines stay on their own."""
    out, open_groups = [], {}
    for task in tasks:
        if "objective" not in task:
            out.append(task)
            continue
        short = shorten(task["objective"], zone_names)
        if task.get("passive") or task.get("idol"):
            # The hand-in that pays out: its own line, so it can be ticked (and auto-ticked) by itself.
            text = f"{short} (completes {task['quest']})" if task["type"] == "main" else f"{task['quest']}: {short}"
            out.append({**task, "text": text})
            continue
        key = (task["type"], task["quest"] if task["type"] == "side" else None)
        group = open_groups.get(key)
        if group is None or len(group["items"]) >= per_line:
            group = {"type": task["type"], "quest": task["quest"], "order": task["order"], "items": []}
            open_groups[key] = group
            out.append(group)
        if not group["items"] or group["items"][-1] != short:
            group["items"].append(short)
    for task in out:
        if "items" in task:
            line = " \u2192 ".join(task.pop("items"))
            task["text"] = f"{task['quest']}: {line}" if task["type"] == "side" else line
    return out


def build_route(route, strict, zones, quests, warnings):
    by_name = {}
    for z in zones:
        by_name.setdefault(z["name"], z)
    by_key = {SCENE_KEY_OVERRIDES[z["scene"]]: z for z in zones if z["scene"] in SCENE_KEY_OVERRIDES}

    steps = []  # flat list of dicts: chapter, era, zone, key, tasks, ...
    for chapter_number, era, visits in route:
        seen = {}
        for visit in visits:
            zone, _, key = visit.partition("@")
            seen[zone] = seen.get(zone, 0) + 1
            info = by_key.get(key) if key else by_name.get(zone)
            if info is None:
                warnings.append(f"route zone not on the site: {zone}")
            steps.append({
                "chapter": chapter_number, "era": era, "zone": zone, "key": key or None,
                "visit": seen[zone], "info": info or {}, "tasks": [],
            })
    route_zones = {s["zone"] for s in steps}
    zone_names = {normalise(z["name"]) for z in zones}

    def chapter_start(number):
        return next((i for i, s in enumerate(steps) if s["chapter"] >= number), len(steps))

    def find(zone, start, limit=30):
        for i in range(start, min(len(steps), start + limit)):
            if steps[i]["zone"] == zone:
                return i
        return None

    passive = idol = 0
    for quest in quests:
        name = quest["name"]
        if name in IGNORED_QUESTS or not quest["objectives"]:
            continue
        dungeon = name in DUNGEON_QUESTS
        if dungeon and DUNGEON_QUESTS[name] not in route_zones:
            continue
        position = chapter_start(quest["chapter"])
        rewarding = quest["passive"] > 0 or quest["idol"] > 0
        useful = (quest["passive"] > 0 and passive < PASSIVE_CAP) or (quest["idol"] > 0 and idol < IDOL_CAP)
        reward_text = ", ".join(filter(None, [
            f"+{quest['passive']} Passive" if quest["passive"] else "",
            "+1 Idol slot" if quest["idol"] else ""]))

        if not quest["main"] and not dungeon and not (useful and name not in SLOW_QUESTS) and name not in KEEP_QUESTS:
            # Mention it once where it is picked up so the player knows it is safe to ignore.
            at = find(quest["start"], position) or find(quest["objectives"][0]["zone"], position)
            if at is None:
                continue
            if name in SLOW_QUESTS:
                text = f"Skip: {name} ({reward_text}, but long - you reach the cap without it)"
            elif rewarding:
                text = f"Optional: {name} ({reward_text} - only counts if you are below the cap)"
            else:
                text = f"Skip: {name} (xp/gold only)"
            steps[at]["tasks"].append({"type": "skip", "text": text, "order": 90})
            continue

        kind = "main" if quest["main"] else "side"
        placements = []  # (step index, task) - applied only once we know the quest fits this route
        accept = None
        if not quest["main"] and not dungeon:
            at = find(quest["start"], position)
            if at is not None:
                accept = (at, {"type": "side", "text": f"Accept: {name}", "quest": name, "order": 50})
                position = at

        zone = quest["start"]
        step_number, step_start = -1, position
        wanted = 0
        final_placed = False
        for objective in quest["objectives"]:
            # Objectives of one quest step can be done in any order, so each is searched for from
            # where the step began rather than from the previous objective.
            if objective["step"] != step_number:
                step_number, step_start = objective["step"], position
            override = OBJECTIVE_ZONE.get((name, objective["text"]))
            if override == "":
                continue
            wanted += 1
            final_placed = False
            zone = override or objective["zone"] or zone
            at = find(zone, step_start)
            if at is None:
                if not strict:
                    continue  # this part of the quest lies in a skipped stretch
                warnings.append(f"{name}: no '{zone}' visit after step {step_start} ({steps[step_start]['zone']}) for: {objective['text']}")
                at = step_start
            position = max(position, at)
            text = objective["text"] if quest["main"] else f"{name}: {objective['text']}"
            task = {"type": kind, "text": text, "quest": name, "objective": objective["text"], "order": 10 if quest["main"] else 60}
            if is_travel_only(objective["text"], steps[at]["zone"]):
                task["travel"] = True
            placements.append((at, task))
            final_placed = True

        # On a skip route a quest that mostly happens in skipped zones is left out altogether.
        if not placements or len(placements) * 2 < wanted:
            continue
        if accept:
            steps[accept[0]]["tasks"].append(accept[1])
        for at, task in placements:
            steps[at]["tasks"].append(task)

        if rewarding and final_placed:
            last = placements[-1][1]
            last.pop("travel", None)
            # Past the cap the game grants nothing, so no badge - keeps the counters honest.
            if quest["passive"] and passive < PASSIVE_CAP:
                last["passive"] = min(quest["passive"], PASSIVE_CAP - passive)
                passive += last["passive"]
            if quest["idol"] and idol < IDOL_CAP:
                last["idol"] = 1
                idol += 1

    chapters = []
    for chapter_number, era, _ in route:
        out_steps = []
        for step in (s for s in steps if s["chapter"] == chapter_number):
            tasks = [t for t in step["tasks"] if not t.get("travel")]
            tasks.sort(key=lambda t: t["order"])  # stable: keeps quest order within a group
            tasks = compress(tasks, zone_names)
            go = GO.get((chapter_number, step["zone"], step["visit"]))
            if go:
                tasks.append({"type": "go", "text": go})
            tips = TIPS.get((chapter_number, step["zone"], step["visit"]), [])
            resist = RESIST.get((chapter_number, step["zone"], step["visit"]))
            if resist:
                tasks.append({"type": "res", "text": resist})
            for kind, text in tips:
                tasks.append({"type": kind, "text": text})
            named_cache = any("Cache" in text for _, text in tips)
            if step["info"].get("cache") and step["visit"] == 1 and not named_cache:
                tasks.append({"type": "tip", "text": "A one-shot loot cache is hidden in this zone"})
            if not tasks:
                tasks.append({"type": "go", "text": "Pass through to the next zone"})
            out = {"zone": step["zone"]}
            if step["key"]:
                out["sceneKey"] = step["key"]
            if step["info"].get("level"):
                out["level"] = step["info"]["level"]
            if step["info"].get("waypoint"):
                out["waypoint"] = True
            out["tasks"] = [{k: v for k, v in t.items() if k in ("type", "text", "quest", "passive", "idol")} for t in tasks]
            out_steps.append(out)
        chapters.append({"id": chapter_number, "title": f"Chapter {chapter_number}", "era": era, "steps": out_steps})

    return chapters, steps, (passive, idol)


# Order for a new character: four timelines give the 5 Knowledge of Orobyss that open the level 90
# ones, one of which unlocks Empowered; the rest can wait.
MONOLITH_ORDER = [
    "Fall of the Outcasts", "The Stolen Lance", "The Black Sun", "Reign of Dragons",
    "The Age of Winter", "Spirits of Fire", "The Last Ruin",
    "Blood, Frost, and Death", "Ending the Storm", "Fall of the Empire",
]
MONOLITH_SCENE = re.compile(r"quest echoes (R\d+Q10), (R\d+Q20), (R\d+Q30)")


def load_endgame():
    """Chapter 11: the Monolith checklist, from tools/monolith.json (timelines, bosses, blessings).

    Returns the chapter and the scene ids of each timeline's quest echoes.
    """
    path = Path(__file__).resolve().parent / "monolith.json"
    if not path.exists():
        return [], {}
    timelines = {t["name"]: t for t in json.loads(path.read_text(encoding="utf-8"))["timelines"]}

    def note(timeline, prefix):
        return next((n[len(prefix):].strip() for n in timeline["notes"] if n.startswith(prefix)), "")

    steps = [{
        "zone": "The End of Time",
        "waypoint": True,
        "tasks": [
            {"type": "main", "text": "Enter the Monolith of Fate - the seven timelines below level 90 are all open"},
            {"type": "main", "text": "Goal: 5 Knowledge of Orobyss to open the level 90 timelines (Chapter 9 and 10 give 1 each)"},
            {"type": "tip", "text": "Order: Fall of the Outcasts, The Stolen Lance, The Black Sun, Reign of Dragons, then one level 90 timeline"},
            {"type": "tip", "text": "Each timeline: run echoes for stability, do the 3 quest echoes in order - the third is the boss"},
            {"type": "tip", "text": "A boss kill offers blessings; one slot per timeline. Re-pick discovered blessings at Chronomancer Nyx"},
            {"type": "tip", "text": "Not joined an item faction yet? Maj'elka Upper District (Chapter 9), talk to Zerrick"},
            {"type": "tip", "text": "Join the Woven: finish a Cemetery echo in any timeline and talk to Masque in the Haven of Silk"},
            {"type": "tip", "text": "Loot filter: the leveling (Regular) tier is right for all of the normal Monolith"},
        ],
    }]
    scenes = {}
    first_ninety = True
    for name in MONOLITH_ORDER:
        timeline = timelines.get(name)
        if timeline is None:
            continue
        echoes = note(timeline, "Quest echoes (stability needed, normal/empowered):").split(". ")[0]
        tasks = [{"type": "main", "text": f"Quest echoes (stability normal/empowered): {echoes}"},
                 {"type": "boss", "text": timeline["boss"]}]
        knowledge = note(timeline, "Knowledge of Orobyss for first completion:").rstrip(".")
        if knowledge and knowledge[0].isdigit():
            tasks.append({"type": "main", "text": f"First completion: +{knowledge} Knowledge of Orobyss"})
        if timeline["level"] >= 90:
            tasks.append({"type": "tip", "text": "Needs the Knowledge of Orobyss quest (5 Knowledge, shared across your characters)"})
            if first_ninety:
                tasks.append({"type": "main", "text": "Beat the Harbinger in the boss echo, then open the chest on the centre island: unlocks Empowered (level 100) timelines"})
                first_ninety = False
        for blessing in (b for b in timeline["blessings"] if b.get("recommended")):
            tasks.append({"type": "tip", "text": f"Blessing: {blessing['name']} - {blessing['effect'].split(' | ')[0]}"})
        rewards = note(timeline, "Exclusive echo rewards (unique/set):").rstrip(".")
        if rewards:
            tasks.append({"type": "tip", "text": f"Echo reward exclusive to this timeline: unique/set {rewards}"})
        steps.append({"zone": name, "level": timeline["level"], "tasks": tasks})
        ids = MONOLITH_SCENE.search(" ".join(timeline["notes"]))
        if ids:
            scenes.update({scene: name for scene in ids.groups()})

    steps.append({
        "zone": "Before Empowered",
        "tasks": [
            {"type": "main", "text": "5 Knowledge of Orobyss, one level 90 timeline beaten, Harbinger killed, centre chest opened"},
            {"type": "res", "text": "75% in every resistance and 100% critical strike avoidance before you step in"},
            {"type": "main", "text": "Switch your loot filter to a Strict tier - level 100 zones drop far too much for a leveling filter"},
            {"type": "tip", "text": "Item faction joined and ranked up; Woven joined"},
            {"type": "tip", "text": "Pick the blessings your build wants before pushing corruption (planner: Targets)"},
        ],
    })
    steps.append({
        "zone": "Empowered Monolith",
        "level": 100,
        "tasks": [
            {"type": "main", "text": "Re-run timelines at level 100 for Grand blessings (same stat, higher roll)"},
            {"type": "main", "text": "Raise corruption: kill the Shade of Orobyss echo; boss kills add Gaze of Orobyss for a bigger step"},
            {"type": "tip", "text": "Cap resistances at 75%; Grand Survival of Might (Reign of Dragons) is a top source of crit avoidance"},
            {"type": "tip", "text": "1.5: Blood Crystal encounters count kills - woven echoes at 750 / 2,000 / 4,000 lead to Morditas"},
            {"type": "tip", "text": "Aberroth: kill all 10 Harbingers in empowered timelines, then use a Harbinger Eye at the Shattered Road"},
        ],
    })
    return [{"id": 11, "title": "Monolith of Fate", "era": "Endgame", "steps": steps}], scenes


def build_endgame_data():
    """Data/endgame.json: timelines with their blessings, and the dungeons, for the planner window."""
    path = Path(__file__).resolve().parent / "monolith.json"
    timelines = []
    if path.exists():
        source = {t["name"]: t for t in json.loads(path.read_text(encoding="utf-8"))["timelines"]}
        for name in MONOLITH_ORDER:
            t = source.get(name)
            if t is None:
                continue

            def note(prefix):
                return next((n[len(prefix):].strip().rstrip(".") for n in t["notes"] if n.startswith(prefix)), "")

            knowledge = note("Knowledge of Orobyss for first completion:")
            blessings = []
            for b in t["blessings"]:
                normal, _, grand = b["effect"].partition(" | Grand (empowered): ")
                blessings.append({"name": b["name"], "effect": normal, "grand": grand, "recommended": bool(b.get("recommended"))})
            timelines.append({
                "name": name, "level": t["level"], "boss": t["boss"],
                "knowledge": int(knowledge[0]) if knowledge[:1].isdigit() else 0,
                "echoes": note("Quest echoes (stability needed, normal/empowered):").split(". ")[0],
                "harbinger": note("Harbinger:"),
                "rewards": note("Exclusive echo rewards (unique/set):"),
                "blessings": blessings,
            })
    return {"knowledgeNeeded": 5, "timelines": timelines, "dungeons": DUNGEONS, "reference": REFERENCE}


def main():
    refresh = "--refresh" in sys.argv
    zones = load_zones(refresh)
    known = {z["scene"] for z in zones}
    for scene, (name, level, waypoint) in EXTRA_ZONES.items():
        if scene not in known:
            zones.append({"scene": scene, "name": name, "era": "", "level": level, "waypoint": waypoint, "cache": False})
    quests = load_quests(refresh)
    endgame, endgame_scenes = load_endgame()

    warnings, routes, all_steps = [], [], []
    for definition in ROUTES:
        strict = definition["id"] == "full"
        chapters, steps, totals = build_route(definition["route"], strict, zones, quests, warnings)
        all_steps += steps
        routes.append({
            "id": definition["id"], "name": definition["name"], "description": definition["description"],
            "chapters": chapters + endgame,
        })
        count = sum(len(c["steps"]) for c in chapters)
        print(f"{definition['id']:9} {count:3} steps; grants {totals[0]} passives, {totals[1]} idol slots")

    for key in list(TIPS) + list(GO) + list(RESIST):
        if not any(s["chapter"] == key[0] and s["zone"] == key[1] and s["visit"] == key[2] for s in all_steps):
            warnings.append(f"tip for unknown visit: {key}")

    guide = {"gameVersion": GAME_VERSION, "passiveCap": PASSIVE_CAP, "idolCap": IDOL_CAP, "routes": routes}
    scenes = {z["scene"]: SCENE_KEY_OVERRIDES.get(z["scene"], z["name"]) for z in zones if z["name"]}
    scenes.update(endgame_scenes)

    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "guide.json").write_text(json.dumps(guide, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    (OUT / "scenes.json").write_text(json.dumps(scenes, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    (OUT / "endgame.json").write_text(json.dumps(build_endgame_data(), indent=1, ensure_ascii=False) + "\n", encoding="utf-8")

    print(f"{len(zones)} zones, {len(quests)} quests")
    for warning in dict.fromkeys(warnings):
        print("WARNING:", warning)


if __name__ == "__main__":
    main()
