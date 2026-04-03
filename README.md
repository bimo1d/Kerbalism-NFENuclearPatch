# Kerbalism NFE Nuclear Patch

Mod compatibility patch between [Kerbalism](https://github.com/Kerbalism/Kerbalism) and [Near Future Electrical](https://github.com/post-kerbin-mining-corporation/NearFutureElectrical).

## Current Version: 0.1a

Download: [GitHub Releases](https://github.com/bimo1d/Kerbalism-NFENuclearPatch/releases)

Docs & support: GitHub issues (forum thread will be added later)

License: Unlicense (public domain)

KSP version: 1.12.5

Requires:
- Kerbalism
- KerbalismConfig (ProfileDefault)
- Near Future Electrical
- SystemHeat

## What This Patch Does

- Enables stable background Electric Charge generation for NFE fission reactors when vessels are unloaded.
- Synchronizes reactor process state with Kerbalism background simulation.
- Keeps reactor resource consumption/production consistent in background processing.

## Download And Installation

1. Download the latest release from GitHub Releases.
2. Extract `GameData/KerbalismNFEFRpatch` into your KSP `GameData` folder.
3. If you use other compatibility packs, avoid duplicate patches that modify the same reactor bridge behavior.

## Mod Compatibility And Support

This patch currently targets NFE fission reactor parts (`nfe-reactor-*`) with Kerbalism ProfileDefault.

Compatibility with third-party compatibility packs (for example `KerbalismCompatibilityOverhaul` and `KerbalismFTT`) is not fully validated yet.
In theory it should work together, but results can depend on patch load order and whether those packs are outdated for your current mod set.

If you find an issue, open a GitHub issue with:
- KSP.log excerpt
- Installed mod list
- short reproduction steps

## Disclaimer And License

This mod is released under the Unlicense, which means it is in the public domain.