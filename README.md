# Last Epoch Helper

Campaign-overlay til Last Epoch (patch 1.5 / Season 5), inspireret af Exile UI's act tracker til PoE.
Viser hvad du skal i den zone du står i, hvilke side quests der giver passive points / idol slots,
hvilke du roligt kan springe over, og hvad dit build skal have på dit nuværende level.

Overlayet er et helt almindeligt Windows-vindue, der ligger øverst. Det læser kun spillets egen
logfil (`Player.log`) og rører hverken spillets hukommelse eller filer.

## Brug

1. Sæt spillet til **Borderless Windowed** (ikke eksklusiv fullscreen – så kan intet overlay tegne ovenpå).
2. Hent den nyeste `LastEpochHelper-x.y.z.zip` under [Releases](https://github.com/Dingobanana/LastEpochHelper/releases),
   pak hele mappen ud et fast sted og start `LastEpochHelper.exe`. Der kommer et "LE"-ikon ved uret.
   Første gang viser Windows "Windows beskyttede din pc" (programmet er ikke signeret): **Flere oplysninger** → **Kør alligevel**.
3. Spil. Overlayet skifter selv step, når du går ind i en ny zone, og skjuler sig, når spillet ikke har fokus.

| Genvej | Funktion |
| --- | --- |
| `Ctrl+Shift+Right` / `Ctrl+Shift+Left` | Næste / forrige step |
| `Ctrl+Shift+H` | Skjul / vis |
| `Ctrl+Shift+L` | Lås (klik går igennem til spillet) / lås op |
| `Ctrl+Shift+C` | Skifter mellem boks, kompakt boks og vandret bjælke |
| `Ctrl+Shift+G` | Planlægger: gear, idols, loot filter, Monolith, dungeons |
| `Ctrl+Shift+T` | Build-træ |
| `Ctrl+Shift+E` | Tjek det item, musen peger på, mod buildets affixes |
| `Ctrl+Shift+M` | Zonekort: fra / i overlayet / stort midt på skærmen |
| `Ctrl+Shift+S` | Gem et skærmbillede af spillet som kort for den zone du står i |

Ulåst kan du trække i toppen for at flytte vinduet og klikke på en linje for at krydse den af.
`☰` hopper til et kapitel, skifter rute eller karakter; `⚙` åbner indstillinger (genveje, størrelse,
gennemsigtighed, build-plan). Tray-ikonet viser/skjuler overlayet og kan lukke programmet.

Linjetyper: `▸` main quest · `◆` side quest der er værd at tage · `☠` boss · `•` tip · `✕` kan springes over.
Gul/lilla markering = questen giver passive point / idol slot.

## Opdateringer

Appen spørger GitHub efter en ny version ved start og hver sjette time. Er der en, står der en grøn linje øverst
i overlayet; klik på den (eller brug **Look for update** under `⚙`) for at hente og installere. Overlayet genstarter
selv, og et "What's new"-vindue viser, hvad der er ændret siden din forrige version. Intet installeres uden at du
beder om det, og der hentes kun zip-filer fra dette repos egne releases. Dit fremskridt ligger i
`%APPDATA%\LastEpochHelper` og røres ikke.

## Funktioner

- **Karakterprofiler.** Hver karakter har sit eget fremskridt, sin egen rute, tid og build-plan. En ny
  karakter får automatisk en profil med sit navn. Loggen navngiver kun en karakter, når den oprettes;
  ellers genkendes den på klasse, mastery og level (to karakterer med samme klasse og næsten samme level
  kan derfor forveksles – skift i så fald under `☰` → Character).
- **Ruter.** `Full campaign`, eller to alt-ruter der springer kapitler over via dungeons
  (Lightless Arbor + Soulfire Bastion, eller Lightless Arbor + Temporal Sanctum). Passive/idol-regnskabet
  regnes om pr. rute.
- **Unclaimed rewards.** Side quests med passive point / idol slot, som du har forladt uden at krydse af,
  bliver stående, til du klikker (taget) eller højreklikker (sprunget over). Main quest-belønninger tælles
  automatisk, når du er forbi dem.
- **Automatisk afkrydsning.** Spillet logger navnet på nogle (ikke alle) quest-trin; de krydses af, når
  navnet entydigt matcher en linje.
- **Build-plan.** Indsæt et Maxroll planner- eller guide-link i indstillingerne og tryk *Import from
  Maxroll*. Rækkefølgen af points er præcis den, forfatteren klikkede; de viste levels er estimater.
  Klik krydser en linje af, højreklik krydser alt af til og med det level. Du kan også skrive din egen plan
  som tekstfil (`<level>: <tekst>` pr. linje) i `%APPDATA%\LastEpochHelper\builds`.
- **Build-træ.** Med et importeret Maxroll-build åbner et vindue med passive-træet og skill-træerne tegnet
  som i spillet (ikoner, forbindelser, `har/skal have` under hver node). Grøn ring med tal = de næste points i
  rækkefølge; guld = taget; stiplet = kommer senere; udtonet = bruges ikke i buildet. Vinduet åbner, når du
  trykker spillets egne taster for passives (`P`) og skills (`S`), og lukker på samme tast eller `Esc`;
  `Ctrl+Shift+T` åbner/lukker det også. Tasterne aflyttes kun – de når stadig spillet – og kan ændres eller
  slås fra under `⚙`. Passive points regnes ud fra level + quest-belønninger; skill points kan loggen ikke
  se, så brug `+`/`−` i vinduet, når du bruger et skill point (eller hvis passive-tallet er skævt).
  Når du åbner et skill-træ i spillet, skifter vinduet selv til den skill: programmet læser spillets vindue
  med Windows' indbyggede tekstgenkendelse og finder skill-navnet i overskriften (kan slås fra under `⚙`).
  Nederst er der to kontakter, som huskes hver for sig for passives og skills: `① order` (tallene, der viser
  rækkefølgen) og `+N points` (hvor mange points der skal i noden i dette skridt). "Next"-linjen viser altid begge dele.
  Ikonerne hentes fra Maxroll ved import og kræver Windows' WebP-codec; uden den tegnes noderne uden billeder.
- **Grønne linjer i overlayet** er påmindelser for dit level (ikke quests): nye skill-slots, resistances og
  skift af loot filter. Klik = gjort; højreklik = gjort inkl. alle tidligere linjer. Buildets egne trin pr. level
  kan også vises som tekst her (slået fra som standard; build-træet viser det samme som billede).
- **Planlægger (`▤`).** Fem sider, der ikke hører til en bestemt zone:
  *Gear* (hvad buildet har på i dit stadie, hvilke affixes du skal kigge efter, kryds pr. slot),
  *Idols* (buildets idols og blessings, med hvilken timeline hver blessing kommer fra),
  *Loot filter* (giv hvert installeret filter et level, så siger overlayet til, når du skal skifte),
  *Monolith* (kryds normal/empowered af pr. timeline, vælg din blessing, hold styr på corruption; Knowledge of
  Orobyss tælles sammen) og *Dungeons* (indgang, boss-mekanik, belønning, skip-udgang, nøglekilder, dine nøgler).
  Monolith- og dungeon-siderne føres i hånden; spillets egen Nexus-oversigt i 1.5 viser en del af det samme.
- **Loot filter fra buildet.** Planlæggerens Loot filter-side skriver et leveling-filter ud fra det importerede
  build direkte i spillets mappe (vælg det i spillet med Shift+F). Det viser alt til level 9, rares til level 29
  og derefter kun items med buildets affixes; uniques, sets og exalted skjules aldrig. `check` ud for et filter
  viser uniques og affixes, der er nyere end noget filteret kender. Kryds flere importerede builds af for ét
  fælles filter til flere karakterer. `+ build` laver en kopi af et eksisterende filter, hvor det dine builds
  vil have (uniques, items med to af deres affixes, idols) aldrig skjules; originalen røres ikke.
- **Search.** Færdige søgestrenge til stash-søgningen ud fra buildets affixes og items; klik for at kopiere.
- **Targets.** Hvilken timeline der giver hver blessing og unique, buildet vil have, med roll-intervaller.
- **Item-tjek.** Hold musen over et item og tryk `Ctrl+Shift+E`: overlayet læser tooltippet fra skærmen og
  siger, hvilke af buildets affixes der er på.
- **Dødsjournal.** Hvert dødsfald logges med zone og level; klik for at notere årsagen.
- **Morditas og Prophecies.** Opslagssider for de nye 1.5-systemer (fra Maxrolls gengivelse af patch notes).
- **Boss-kort.** Bosser i zonen står i en rød boks, som også vises i kompakt visning.
- **Skjold-linjer (`🛡`).** Hvilke skadetyper du møder i hver æra, og påmindelser om resistances ved level 25 og 55.
  Det er grove træk ud fra fjendetyperne, ikke data pr. monster.
- **Bjælke-layout.** Hele steppet på én linje hen over skærmen, til streaming eller små skærme.
- **Milestones.** Uden plan vises stadig, hvornår nye skill specialization slots låses op.
- **Tid.** Spilletid pr. karakter (kun mens du er inde i verdenen), tid i nuværende kapitel, bedste tid
  for kapitlet blandt dine andre karakterer på samme rute, og antal dødsfald.
- **Zonekort.** Åbn kortet i spillet og tryk `Ctrl+Shift+S`; billedet vises fremover, når du er i den zone.
  Billederne ligger i `%APPDATA%\LastEpochHelper\maps` og kan også lægges ind i hånden (`<zonenavn>.png`).
- **Monolith.** Efter kapitel 10 følger en tjekliste med timelines i anbefalet rækkefølge, quest echoes,
  bosser og udvalgte blessings.

## Sådan virker det

- Spillet skriver `Scene Z32 load started: load mode: Single` i `Player.log` ved hvert zoneskift.
  `Data/scenes.json` oversætter scene-id til zonenavn, og trackeren hopper til nærmeste step i den zone
  (højst et par steps frem eller tilbage, så en tur i byen ikke sender dig langt væk).
- Ukendte scener (fx nye zoner i en patch) bindes automatisk til næste step og gemmes i
  `%APPDATA%\LastEpochHelper\learned_scenes.json`. Var gættet forkert, så tryk "forrige" med det samme.
- Quest-belønninger er delt: de første 15 passive points og 8 idol slots du får fra quests tæller,
  uanset hvilke quests. Ruten når begge lofter i kapitel 6–7; derefter er side quests markeret valgfri.
- Alt gemmes i `%APPDATA%\LastEpochHelper` (`profiles.json`, `settings.json`). Fejl skrives til `errors.log`.

## Kilder og forbehold

- Quest-trin, belønninger, zone-levels og scene-id'er er datamined (<https://lastepoch.tunklab.com>,
  build `1.5-preview1`). Belønningerne stemmer med EHG's officielle support-artikel for kapitel 1–9.
- Lofterne 15/8 er ikke officielt bekræftet for 1.5. De tre dungeon-nøgle-caches fra 1.5 er udledt af
  datamine; patch notes nævner ikke zonerne.
- Alt-ruterne er sat sammen ud fra dataminens udgange og er ikke spillet igennem; quest-loggen efter et
  skip kan afvige fra listen.
- Monolith-data: timelines og blessings fra tunklab, anbefalinger fra Maxroll (dateret 1.4).

## Opdatering af guiden efter en patch

Ruten (zonerækkefølge), til-/fravalg af side quests og tips er skrevet i hånden i `tools/build_guide.py`;
resten hentes.

```
python tools/build_guide.py --refresh    # henter siderne igen og skriver Data/guide.json + scenes.json
dotnet test
```

## Ny version

1. Sæt `<Version>` i `src/LastEpochHelper/LastEpochHelper.csproj` og skriv en sektion `## x.y.z - titel` øverst i `CHANGELOG.md`.
2. Commit og push.
3. `pwsh tools/release.ps1` kører testene, bygger zip-filen og opretter GitHub-releasen `vx.y.z` med changelog-sektionen
   som tekst. Kørende installationer finder den selv.

Scriptet skriver en `WARNING`, hvis et quest-trin ikke kan placeres på den fulde rute.
`tools/monolith.json` er kilden til Monolith-kapitlet.
