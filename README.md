# Sarkastic.eu Dedicated Simulation

> **Fork of [Serverside Simulations](https://github.com/ddormer/valheim-serverside)** by ddormer, which is no longer maintained as of 2026, renamed at the original authors' request. Updated for Valheim 1.0, building on [ddormer/valheim-serverside#118](https://github.com/ddormer/valheim-serverside/pull/118) by @mreastman.

Run world and monster simulations on a **dedicated server**.

**Status (1.3.0):** passed a basic live test on a Windows dedicated server running Valheim 1.0.7 with up to four players: joining, chests, picking up items, harvesting, mining and combat, with no exceptions or mod warnings. Not yet covered: Frost Foundry, sailing, raids, Deep North events and non-default `-simulationdistance`.

Updated for patch: 1.0.7

On startup the mod checks whether the vanilla methods it replaces have changed since this version was reviewed, and logs a warning naming the method if so. After a game update, look for `Vanilla ... changed` in `BepInEx/LogOutput.log`.

### Features
- Server simulates world and AI physics.
- Client FPS improvements
- Ships are simulated by the driver to improve the steering experience with high latency.

### Installation

 1. Install BepInEx (optionally installing "Better Networking" on both clients and the server is recommended)
 2. Copy `SarkasticEU_Dedicated_Simulation.dll` into the BepInEx/plugins/ directory on your dedicated server.
 3. You're done! No client-side changes are needed.

**Upgrading from Serverside Simulations:** delete `Serverside_Simulations.dll` from BepInEx/plugins/. Both use the same plugin GUID, so only one of them can load; the config file `MVP.Valheim_Serverside_Simulations.cfg` carries over.


_It's recommended to also install the mod "BetterNetworking", it works very well with this mod._

### Configuration

- `[Server] ConsoleCommands` (default on) reads `save` and `stop` from the server's standard input. A vanilla server ignores its input, so panels stop it by closing or killing the process, and on Windows that skips the world save: everything since the last autosave is lost. **AMP:** set `App.ExitMethod=String` in the instance's `GenericModule.kvp` (the Valheim template already has `App.ExitString=stop`), and set `[Logging.Console] Enabled = false` in `BepInEx/config/BepInEx.cfg`, otherwise BepInEx opens its own console and takes over standard input. `save` can then be typed into the AMP console or scheduled.
- `[Server] UnityJobWorkers` (default 8) caps Unity's job worker threads. Unity starts one per CPU core and the idle ones still use CPU; on a 24-thread machine an idle server went from 108% to 38% of a core. Only ever lowers the count; 0 leaves Unity's default.
- `[Networking]` raises the limits on how fast the server sends world data to each player: the per-player send queue (Valheim: 10 KB, default here 32 KB) and Steam's send rate (Valheim: 150 KB/s, default here 256–1024 KB/s). Keep `SteamSendRateMinKB` × players below the server's upload speed. Every `StatsIntervalMinutes` the log shows, per player, how often their send queue was full; if that stays near 0 % the limits are not what holds you back. This is the server-side part of [BetterNetworking](https://github.com/CW-Jesse/valheim-betternetworking) by CW-Jesse (MIT); do not run both. Its compression is not included, as it needs the mod on clients too.

- MaxObjectsPerFrame.MaxObjects can be increased to improve the loading times of areas on the server, at the expense of CPU usage.

### Caveats

- Only runs on dedicated servers.
- The mod dramatically increases server resource usage and running it on a weak CPU or limited RAM may lead to a poor gameplay experience.
- The mod should be disabled when using the "optterrain" command. 
- This mod does not prevent cheating or any kind of client manipulation.
- While this mod is quite light on complexity, as with most mods it's possible future Valheim patches will break the mod in unexpected ways. We recommend you back up your characters and worlds and/or consider disabling this mod anytime a new game patch is released.

### Why?

Ordinarily, to keep server resource usage low, the Valheim server will hand off simulation of an area to the first client that enters said area. However, if the player in charge of the area has a poor connection all other players in that area will suffer. This mod is an attempt at improving that specific situation at the cost of increased latency for the client which would ordinarily own the area.

### How?

This dedicated server mod causes terrain, monsters and other objects that are normally created and owned by clients to instead be created on—and thus owned and simulated by—the server.

#### For mod developers - compatibility

This mod keeps the plugin GUID of Serverside Simulations, `MVP.Valheim_Serverside_Simulations`, so existing checks for it keep working.

For mod developers interested in maintaining compatibility:
- If your mod makes changes relating to simulation / behaviour of the world, it will need to be able run on the dedicated server and should take these points into account:
  - Player.m_localPlayer is always `null` on a dedicated server; your code should check for this.
  - On a dedicated server, `ZNet.instance.GetReferencePosition()` returns a position outside of the world and is not related to any player position.
  - Any graphical or hud-related code should probably be behind a `ZNet.instance.IsDedicated()` check, if that code is expected to run on the server.

### Manually compiling

To manually compile, create a file at `src/Environment.props` with the following content, and change the path to point at your Valheim install.

```
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <!-- Needs to be your path to the base Valheim folder -->
    <VALHEIM_DEDI_INSTALL>E:\SteamLibrary\steamapps\common\Valheim dedicated server</VALHEIM_DEDI_INSTALL>
  </PropertyGroup>
</Project>
```
