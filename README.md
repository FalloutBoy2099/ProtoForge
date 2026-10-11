# ProtoForge (by Kain)

Prototype editor for Fallout 1 / 2 (`.pro`), a merge and rewrite of **Proto Manager** and **f2wedit**.
.NET 10 · WPF · Fluent UI (WPF-UI) · MVVM (CommunityToolkit.Mvvm).

## Features

- **Opening** a game folder (reads `fallout2.cfg`, `ddraw.ini`, `mods/mods_order.txt`, `patchXXX.dat`), a mod folder or a DAT archive (DAT1 LZSS and DAT2 zlib), without extracting anything. Sources are stacked like the engine does (the first one that has a file wins) and can be reordered in the settings. A Fallout 1 archive switches the formulas and the perk list to Fallout 1.
- **Library**: virtualised list, instant search (`pid:`, `file:`, `name:`, `text:`, `kind:`, `source:`, `flag:`), filters (type, kind, flags, image present/missing, modified, errors, source), attribute column (`R/O`, `BAD!`, `F1`, hit points of critters).
- **Editing**: dynamic panels per type (weapons, armors, ammo, drugs, containers, keys, misc items, critters, scenery, walls, tiles, map objects), multi-selection editing, creation from templates, duplication, deletion of the last prototype of a list, undo/redo, hex view with field overlay, field table (decimal and hexadecimal), FRM previews (ground with the critter animation, inventory, portrait).
- **Item types**: an item (or a scenery object) can be turned into another type; the common data is kept.
- **Original game**: restore the original version of a prototype, or delete the override of the output folder so that the lower layer (usually `master.dat`) is used again.
- **Weapons**: critical failure and sound lists, weapon score (sfall or original formula), sound preview (built-in decoder of the game `.acm` sounds).
- **Armors and critters**: armor score, copy of the defences of an armor into the critter bonus stats, skills with the value computed from SPECIAL and the total, derived stats from the formulas. Transparency modes and big gun / energy weapon are exclusive, like in the original tools.
- **LST and MSG files** kept up to date automatically; built-in message editor; text encodings including Russian DOS 866 and its LevCorp variant.
- **AI packets**: full editor of `data/ai.txt` (all parameters, preferred drugs, taunt sets, raw text of a packet, add and delete packets, other AI files), undoable and saved through the usual save plan.
- **Comparison** side by side (two prototypes, loaded state, original game), in a dedicated window.
- **Validation**, extensible (missing files, PIDs, sizes, images, scripts, out-of-range values, flags, references) with an exportable report.
- **Exchange**: structured JSON export/import, CSV export/import (choice of columns, readable values with names and `true`/`false` flags, lines starting with `#` ignored), bulk creation of walls/tiles (in the game or as standalone files with a list).
- **Art tools**: convert a BMP to a FRM, add inventory or ground images to the game (the FRM and its list entry are written with the next save), save a preview as a BMP (8 bits with the game palette, or 32 bits with transparency), extract the art of the DAT archives to a folder.
- **sfall**: extended flags, AI packets (`data/ai.txt`).
- **Interface**: dark/light/automatic theme and language (11 languages) switchable from the buttons at the top right; changing the language restarts the application and reopens the current project. Complete keyboard navigation (search box, list, editor: `F3`, `F6`, `Esc`, ...).
- **Safety**: no silent writes. Saving lists the files (including the deletions), backs up the replaced files in `.protoforge-backup/` and only writes to the chosen output folder.

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

Optional variables for the integration tests: `PROTOFORGE_TEST_FALLOUT2` (a Fallout 2 folder), `PROTOFORGE_TEST_FALLOUT1_DAT` (a Fallout 1 `master.dat`).

Settings are stored in `%AppData%\ProtoForge` (`settings.json`, `templates`, `plugins`).

The "Homepage" and "Check for updates" buttons of the About page appear when the addresses are set in `ProtoForge.UI/Services/AppInfo.cs`.

## Code style

Defined by `.editorconfig`: block-scoped namespaces, `using` inside the namespace, CLR type names (`Int32`, `String`, `Boolean`…), tabs, CRLF. Check with `dotnet format style ProtoForge.slnx --verify-no-changes`.

## Known limitations

- The binary layout of scenery is deduced from real files; double-check it before editing those files.
- Reading real Fallout 1 DAT files is only tested on a synthetic archive.
- A prototype can only be deleted if it is the last one of its list (otherwise the following PIDs would change); the override of any prototype can be removed.
- The sound decoder is checked against ffmpeg on the sounds of Fallout 2 and the Restoration Project; a few sounds that ffmpeg itself cannot decode are decoded by ProtoForge without a second reference.
