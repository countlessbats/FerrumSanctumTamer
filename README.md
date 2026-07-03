# Ferrum Sanctum Tamer

Unity Mod Manager mod for **Warhammer 40,000: Rogue Trader**.

## Features

- Hides only the Ferrum Sanctum buff/debuff icon from unit overhead UI.
- Leaves all other overhead buffs and debuffs untouched.
- Optional mechanical settings:
  - Vanilla: 15% shock damage increase per stack
  - Reduced: 10% per stack
  - Reduced: 5% per stack
  - Disabled: 0% per stack, block new Ferrum Sanctum applications, and strip existing Ferrum Sanctum buffs from loaded characters
- Restores Ferrum Sanctum buffs removed or blocked by the mod when switching back from 0%.
- Updates the Ferrum Sanctum tooltip at render time so it shows the selected per-stack value.

Changing this setting should immediately affect gameplay.

The Ferrum Sanctum blueprint GUID filtered by the mod is:

```text
780497b04a0944e59cb57e68bb9775c4
```

## Install

### Unity Mod Manager

1. Install Unity Mod Manager for Warhammer 40,000: Rogue Trader.
2. Download `FerrumSanctumTamer-1.0.0.zip`.
3. Open Unity Mod Manager.
4. Go to the Mods tab.
5. Drag the zip into the Mods tab, or use Install Mod and select the zip.
6. Start the game.
7. Open the Unity Mod Manager overlay in-game with `Ctrl+F10`.
8. Configure Ferrum Sanctum Tamer from the mod settings panel.

### Manual Install

Create this folder if it does not already exist:

```text
%USERPROFILE%\AppData\LocalLow\Owlcat Games\Warhammer 40000 Rogue Trader\UnityModManager\FerrumSanctumTamer
```

Copy these files into it:

```text
FerrumSanctumTamer.dll
Info.json
README.md
LICENSE
```

Restart the game after replacing the DLL.

## Build From Source

Requirements:

- Windows
- Warhammer 40,000: Rogue Trader installed locally
- Unity Mod Manager installed for Rogue Trader
- .NET SDK 8 or newer

From the repository root:

```powershell
.\build.ps1 -GamePath "E:\SteamLibrary\steamapps\common\Warhammer 40,000 Rogue Trader"
```

The script:

1. Locates the game's managed assemblies under `WH40KRT_Data\Managed`.
2. Locates `UnityModManager.dll` in the default Rogue Trader Unity Mod Manager folder unless `-UnityModManagerDll` is supplied.
3. Compiles `Source\FerrumSanctumTamer.cs` with Roslyn `csc`.
4. Writes the DLL to `bin\FerrumSanctumTamer.dll`.
5. Creates a release zip in `dist\FerrumSanctumTamer-1.0.0.zip`.

Example with an explicit Unity Mod Manager DLL:

```powershell
.\build.ps1 `
  -GamePath "E:\SteamLibrary\steamapps\common\Warhammer 40,000 Rogue Trader" `
  -UnityModManagerDll "$env:USERPROFILE\AppData\LocalLow\Owlcat Games\Warhammer 40000 Rogue Trader\UnityModManager\UnityModManager.dll"
```

## Audit Notes

This repository is intended to be auditable from source.

- The source is a single C# file: `Source\FerrumSanctumTamer.cs`.
- The mod uses Harmony patches against Rogue Trader UI, buff, tooltip, and damage-modifier classes.
- It does not perform network access.
- It does not read or write arbitrary files. Persistent settings are handled by Unity Mod Manager's standard settings mechanism.
- The release DLL can be reproduced locally with `build.ps1` using a local Rogue Trader install and Unity Mod Manager.

Primary Harmony patch targets:

- `OvertipEntityUnitVM` constructor: track overhead buff UI parts.
- `UnitBuffPartVM.HandleBuffDidAdded`: suppress only Ferrum Sanctum overhead display when enabled.
- `UnitBuffPartVM.UpdateData`: refresh reversible overhead hiding.
- `TooltipTemplateBuff.Prepare`: update Ferrum Sanctum tooltip percentage.
- `BuffCollection.Add`: block Ferrum Sanctum application only in 0% mode.
- `WarhammerDamageModifier.TryApply`: adjust Ferrum Sanctum's mechanical damage percentage.

## License

MIT License. See `LICENSE`.
