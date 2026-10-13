# ProtoForge (by Kain)

Prototype editor for Fallout 1 / 2 (`.pro`), a merge and rewrite of **Proto Manager** and **f2wedit**.
.NET 10 · WPF · Fluent UI (WPF-UI) · MVVM (CommunityToolkit.Mvvm).

## Features

- **Opening** a game folder (reads `fallout2.cfg`, `ddraw.ini`, `mods/mods_order.txt`, `patchXXX.dat`), a mod folder or a DAT archive (DAT1 LZSS and DAT2 zlib), without extracting anything. Sources are stacked like the engine does (the first one that has a file wins) and can be reordered in the settings. A Fallout 1 archive switches the formulas and the perk list to Fallout 1.
- **Library**: virtualised list, instant search in the title bar (`pid:`, `file:`, `name:`, `text:`, `kind:`, `source:`, `flag:`) with completion of the prefixes and of their values (kinds, flags, sources), the number of results, the latest searches and searches you keep with the star; filters (type, kind, flags, image present/missing, modified, errors, source), attribute column (`R/O`, `BAD!`, `F1`, hit points of critters). The editor keeps its fields when you select another prototype of the same kind, and the images around the selection are decoded in advance.
- **Editing**: dynamic panels per type (weapons, armors, ammo, drugs, containers, keys, misc items, critters, scenery, walls, tiles, map objects), multi-selection editing, creation from templates, duplication, deletion of the last prototype of a list, undo/redo with a history list (title bar) to go back to any earlier edit, hex view with field overlay, field table (decimal and hexadecimal), FRM previews (ground with the critter animation, inventory, portrait).
- **Usage** (tab of the editor): where a prototype is used (ammunition of a weapon, projectile, key...), what it uses, which prototypes share its image, script or AI packet, and the AI packets that list a drug as a preferred one. Double-click a line to show that prototype.
- **Item types**: an item (or a scenery object) can be turned into another type; the common data is kept.
- **Original game**: restore the original version of a prototype, or delete the override of the output folder so that the lower layer (usually `master.dat`) is used again.
- **Weapons**: critical failure and sound lists, weapon score (sfall or original formula), sound preview (built-in decoder of the game `.acm` sounds).
- **Armors and critters**: armor score, copy of the defences of an armor into the critter bonus stats, skills with the value computed from SPECIAL and the total, derived stats from the formulas. Transparency modes and big gun / energy weapon are exclusive, like in the original tools.
- **LST and MSG files** kept up to date automatically; built-in message editor (in the prototype texts `pro_*.msg` the name and the description of a prototype share one row); text encodings including Russian DOS 866 and its LevCorp variant. A description can have a line break (two lines at most): it is stored as the `\n` escape that recent sfall versions display.
- **AI packets**: full editor of `data/ai.txt` (all parameters, preferred drugs, taunt sets, raw text of a packet, add and delete packets, other AI files), undoable and saved through the usual save plan.
- **Balance** (page): comparison tables of the weapons (average damage with bars, price per damage, damage per action point, score) and of the armors (score, thresholds, resistances, price per point), with the figures that stand out highlighted, and a combat calculator (weapon, ammunition and target, or typed values) that follows the damage rules of Fallout 2: ammunition scaling, threshold, then resistance.
- **Comparison** side by side (two prototypes, loaded state, original game; also from the context menu of the list), in a dedicated window, with an export of the differences (Markdown or CSV).
- **Validation**, extensible (missing files, PIDs, sizes, images, scripts, out-of-range values, flags, references) with an exportable report.
- **Find and replace** in the names and descriptions (Tools page): by type or range of numbers, preview of the number of occurrences, a single undoable step.
- **Exchange**: structured JSON export/import, CSV export/import (choice of columns, readable values with names and `true`/`false` flags, lines starting with `#` ignored), bulk creation of walls/tiles (in the game or as standalone files with a list).
- **Images by drag and drop**: drop a `.frm` or a `.bmp` on an image field of the editor: the picture is added to the game (a BMP is converted with the palette of the game; an existing name is reused, never replaced) and assigned to the selection.
- **Critter preview** (tab of the editor): the six directions animated side by side, for every weapon and animation the art files of the critter have.
- **Art tools**: convert a BMP to a FRM, add inventory or ground images to the game (the FRM and its list entry are written with the next save), save a preview as a BMP (8 bits with the game palette, or 32 bits with transparency), extract the art of the DAT archives to a folder.
- **sfall**: extended flags, AI packets (`data/ai.txt`).
- **Interface**: dark/light/automatic theme and language (11 languages) switchable from the buttons at the top right; changing the language restarts the application and reopens the current project. Complete keyboard navigation (search box, list, editor: `F3`, `F6`, `Esc`, ...).
- **Safety**: no silent writes. Saving lists the files (including the deletions), backs up the replaced files in `.protoforge-backup/` and only writes to the chosen output folder; a save inside the game folder itself is confirmed once per session even when the confirmation is off.

## Architecture

| Project | Role |
|---|---|
| `ProtoForge.Core` | Binary formats (PRO, DAT, LST, MSG, FRM, BMP, ACM, `ai.txt`), field schemas, domain model, undo/redo, validation, diff, serialisation. No UI dependency. |
| `ProtoForge.Infrastructure` | Layered file system, game configuration discovery, workspace (loading/saving), settings, plug-ins. |
| `ProtoForge.UI` | WPF application (MVVM, dependency injection, localisation in en, de, es, fr, it, pt, ru, sv, pl, cs, zh). |
| `ProtoForge.Core.Tests` | Unit and integration tests (run against a Fallout 2 installation when one is found, skipped otherwise). |

Adding a PRO type or a field = describing it in `ProtoForge.Core/Formats/Pro/ProtoSchemas.cs`: the editor, validation, diff, export, field table and hex view pick it up.
Adding a validation rule: implement `IValidationRule` (or derive from `ValidationRuleBase`) in an assembly dropped in the `plugins` folder.

## Build and run

```bash
dotnet build ProtoForge.slnx
dotnet run --project ProtoForge.UI -- "C:\Games\Fallout2" --output "C:\Games\Fallout2\data"
dotnet test ProtoForge.Core.Tests
```

Optional variables for the integration tests: `PROTOFORGE_TEST_FALLOUT2` (a Fallout 2 folder), `PROTOFORGE_TEST_FALLOUT1` (a Fallout 1 folder with `MASTER.DAT` and `CRITTER.DAT`), `PROTOFORGE_TEST_FALLOUT1_DAT` (a Fallout 1 `master.dat`). The integration tests only read the games: the save tests write into a temporary folder.

`Tools/Smoke.ps1 <game folder>` starts the built application on a game with a temporary output folder and checks, through UI Automation, that the main window opens and the list is filled.

`Publish.ps1` builds the two single-file executables into `publish/` (`ProtoForge.exe` needs the .NET 10 desktop runtime, `ProtoForge-NET.exe` carries it).

Settings are stored in `%AppData%\ProtoForge` (`settings.json`, `templates`, `plugins`).

The "Homepage" and "Check for updates" buttons of the About page appear when the addresses are set in `ProtoForge.UI/Services/AppInfo.cs`.

## Code style

Defined by `.editorconfig`: block-scoped namespaces, `using` inside the namespace, CLR type names (`Int32`, `String`, `Boolean`…), tabs, CRLF. Check with `dotnet format ProtoForge.slnx --verify-no-changes`. XAML files are formatted with [XamlStyler](https://github.com/Xavalon/XamlStyler) and `Settings.XamlStyler`: `xstyler --config Settings.XamlStyler --directory ProtoForge.UI --recursive --passive` checks them (files without BOM, CRLF, no final newline).

Texts shown to the user are never written in the code: use `Loc.Get` in the UI, `UserTexts.Format` below the UI layer (a test checks that every key exists in the 11 languages).

## Known limitations

- The binary layout of scenery is deduced from real files; double-check it before editing those files.
- Fallout 1 is tested on a real installation (every file of `MASTER.DAT` and `CRITTER.DAT` decodes, a save and reload round trip works); writing new DAT archives is not supported (the output is always a folder).
- A prototype can only be deleted if it is the last one of its list (otherwise the following PIDs would change); the override of any prototype can be removed.
- The sound decoder is checked against ffmpeg on the sounds of Fallout 2 and the Restoration Project; a few sounds that ffmpeg itself cannot decode are decoded by ProtoForge without a second reference.
