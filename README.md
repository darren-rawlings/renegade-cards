# renegade-cards

The card-game kit for Renegade Games' Godot C# games. Each card game links this repository in as a
git submodule at `addons/renegade_cards`, next to the studio-wide
[renegade-core](https://github.com/darren-rawlings/renegade-core) (ads, shop, settings, HUD, sound,
dev and release tools). Only card games use this kit, so card work never touches the board games,
and nothing board-only (spinning the board, board and piece materials) is used here.

| Folder | What's in it | Needs Godot? |
|---|---|---|
| `pure/` | `Card`, `Suit`, `Deck` (a fair shuffle) | No: unit tested |
| `godot/` | 3D cards with faces drawn in code, card styles (backs; fronts with their own suits and face cards), the table, shuffle and deal, glass hands, card sounds | Yes |

## Using it in a card game

```
git submodule add https://github.com/darren-rawlings/renegade-cards addons/renegade_cards
```

Keep the kit's tests and build output out of the game assembly in the game's `.csproj`:

```xml
<DefaultItemExcludes>$(DefaultItemExcludes);addons/renegade_cards/tests/**;addons/renegade_cards/**/bin/**;addons/renegade_cards/**/obj/**</DefaultItemExcludes>
```

and test the engine-free part from the game's tests:

```xml
<Compile Include="../../addons/renegade_cards/pure/**/*.cs" Link="Cards/%(RecursiveDir)%(Filename)%(Extension)" />
```
