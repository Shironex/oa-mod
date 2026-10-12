# OA Mod

A BepInEx 6 (IL2CPP) plugin for *Golf With Your Friends* that I use for our own tournaments.

## Please read

- This mod exists only to make playing with friends easier. It is not meant to break the game or to give anyone an edge.
- It contains no game code and distributes none. You need your own full copy of *Golf With Your Friends* to use it.
- If you do not own the game yet, buy it: it is a great game and the people who made it deserve the support. [Golf With Your Friends on Steam](https://store.steampowered.com/app/431240/Golf_With_Your_Friends/)
- It is not affiliated with or endorsed by the game's developers or publisher. I hope joining a match in progress is supported out of the box in Golf With Your Friends 2.
- It comes with no warranty, and I do not promise how long I will keep it working. A game update can break it.

## Features

- **Join a match in progress** (`[Room] KeepOpen`, on by default). The game closes the room when a match starts, so friends have to wait for the next one. With this on, the host reopens it and walks an invited friend into the running match. The room stays hidden from the public browser.
- **Steam avatars for late joiners.** The scoreboard shows their picture instead of a plain ball.
- **Fly camera** (`[FlyCam] ToggleKey`, default F6). A free camera that ignores walls. Mouse to look, WASD to move, Q/E down and up, Left Shift to go faster. Only you see it.

## Who needs it

The host, and everyone who is already in the match when someone else joins. The player who joins can do without, as long as nobody joins after them. In practice: everyone in the tournament.

## Build

Install BepInEx 6 (IL2CPP, be.788) into the game folder and launch the game once, then:

```
dotnet build OaMod.csproj -c Release
```

The build copies `OaMod.dll` into `BepInEx/plugins/OaMod`. Pass `-p:NoDeploy=true` to skip the copy (Windows locks the DLL while the game runs), and `-p:GameDir=...` if the game is not in the default Steam library.

## Known limits

- A friend who joins during the scoreboard keeps it on screen for everyone until their course has loaded.
- Which hole a late joiner starts on is not handled yet.
- The team scoreboard does not get the avatar fix.
