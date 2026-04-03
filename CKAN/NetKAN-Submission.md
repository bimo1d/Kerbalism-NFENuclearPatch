# NetKAN Submission (KSP-CKAN/NetKAN)

Create a new file in `KSP-CKAN/NetKAN`:

`NetKAN/KerbalismNFEFRpatch.netkan`

with the following content:

```json
{
  "identifier": "KerbalismNFEFRpatch",
  "$kref": "#/ckan/netkan/https://github.com/bimo1d/Kerbalism-NFENuclearPatch/raw/main/CKAN/KerbalismNFEFRpatch.netkan"
}
```

Notes:

- This points CKAN to the authoritative metadata in this repository.
- Keep release assets named like `KerbalismNFEFRpatch_*.zip` so `asset_match` continues to work.
