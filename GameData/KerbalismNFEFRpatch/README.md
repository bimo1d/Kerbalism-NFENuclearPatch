# KerbalismNFEFRpatch

A standalone compatibility patch for **Kerbalism** + **Near Future Electrical** fission reactors.

## Why this exists

On some setups, NFE fission reactors can behave correctly while loaded but fail to produce Electric Charge on unloaded vessels (background simulation). This patch keeps the NFE/SystemHeat reactor gameplay on loaded vessels and stabilizes Kerbalism background behavior for unloaded vessels.

## What is included

- `Patches/NearFutureElectrical/KerbalismNFEFRpatch_Reactor_Background_Fix.cfg`
- `Plugins/KerbalismNFEFRpatch.dll`

## Behavior

- Loaded vessel: reactor control stays in NFE/SystemHeat UI.
- Unloaded vessel: Kerbalism process state is synchronized to reactor state.
- `_Nukereactor` pseudo-resource is kept in sync to prevent background EC stalls.

## Requirements

- Kerbal Space Program `1.12.5`
- Kerbalism
- KerbalismConfig (`ProfileDefault`)
- Near Future Electrical
- SystemHeat (required by current NFE reactors)

## Installation

1. Copy `KerbalismNFEFRpatch` into `GameData`.
2. If you previously installed older versions of this patch, remove old files before copying the new version.

## Scope

Current target: NFE fission reactor parts (`nfe-reactor-*`).

## License

Unlicense (public domain).
