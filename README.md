# Infinite Zone Demand

A Cities: Skylines II code mod that keeps zone demand at maximum so zoned lots keep growing buildings. Each zone type can be switched on or off in **Options → Infinite Zone Demand**.

| Option | Default | Effect |
| --- | --- | --- |
| Low / Medium / High density housing | On | Residential building demand held at 100 for that density |
| Keep people moving in | On | Household demand held at 100 or more, so new homes get residents |
| Commercial | On | Commercial demand plus per-product shop demand |
| Industrial | On | Manufacturing demand plus per-product factory demand |
| Office | On | Office demand plus per-product office demand |
| Warehouses | Off | Warehouse demand on industrial zones |
| Build anywhere | Off | Game's built-in fast-spawn mode: ignores demand and location for all zones |

## How it works

Every 16 simulation frames the game's demand systems recompute demand in background jobs. `ZoneSpawnSystem` then reads two things:

1. The headline numbers (the demand bars). These gate whether a zone type is considered at all.
2. **Per-product arrays.** These score each candidate building by the products it sells, makes or stores. A building whose products have no demand is skipped.

Mods that only force the headline numbers fill the bars, but commercial and industrial zones can stay empty. `InfiniteDemandSystem` runs in the spawner's slot, just before it. It completes the demand jobs, then raises both the headline numbers and the per-product arrays. The game recomputes everything from scratch next cycle, so switching an option off returns that zone to vanilla demand within one cycle.

There's no Harmony. Private game fields are read by reflection. If a game update renames one, it's logged once and only that part stops working; the game doesn't crash.

## Build and test (gaming PC)

Requirements:

- Cities: Skylines II (PC).
- The **Modding Toolchain**, installed from the game's **Options → Modding**. It installs the .NET SDK, Unity pieces and the `CSII_*` environment variables this project uses.
- Visual Studio is optional.

```bash
git clone https://github.com/sammcneil29/cs2-infinite-zone-demand.git
cd cs2-infinite-zone-demand/InfiniteZoneDemand
dotnet build -c Release
```

Close the game before building, because it locks the DLL. The build deploys the mod to `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\InfiniteZoneDemand`. The game loads it from there as a local mod.

In game:

1. Check that **Options → Infinite Zone Demand** appears.
2. Load a city with empty zoned lots and unpause.
3. Check the bars fill and buildings grow in every zone type, including commercial and industrial.
4. Check the log `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\InfiniteZoneDemand.Mod.log`. It should contain `All demand-system fields found.` and `Resources classified: ...`. Any `Game fields not found` or `failed` lines mean a game update changed something.

This project was written without access to the game files, against the 1.6.2f1 game internals as documented by other open-source mods. The first build on the gaming PC is the real compile check.

## Publish to Paradox Mods

1. Launch the game once and log in to your Paradox account, then close it. The publisher reuses that login and never asks for a password.
2. Open your profile on [mods.paradoxplaza.com](https://mods.paradoxplaza.com) once so it has a display name.
3. Review `InfiniteZoneDemand/Properties/PublishConfiguration.xml` (title, descriptions, thumbnail, `AccessLevel`).
4. Publish:

   ```bash
   dotnet publish -c Release -p:PublishProfile=PublishNewMod
   ```

5. Copy the printed `Mod published with Id=NNNNNN` into `<ModId Value="NNNNNN" />` and commit it. Without it, the next publish creates a duplicate listing.
6. Delete the local copy in `...\Cities Skylines II\Mods\InfiniteZoneDemand`, so the subscribed version is the one that loads.

For later updates, bump `ModVersion`, set `ChangeLog`, then:

```bash
dotnet publish -c Release -p:PublishProfile=PublishNewVersion
```

To change only the description or images, without a new build:

```bash
dotnet publish -c Release -p:PublishProfile=UpdatePublishedConfiguration
```

## Compatibility

- Don't combine it with other demand overrides: Super Fast Building And Leveling's demand sliders, Unlimited Demand, InfiniteDemand.
- Mods that replace the vanilla demand or spawn systems may change the result: MapExt2 replaces the demand jobs, and Realistic Workplaces And Households replaces household spawning.

## License

MIT
