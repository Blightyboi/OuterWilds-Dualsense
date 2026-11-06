<p align="center"><img src="assets/logo.jpg" alt="OuterWilds-Dualsense" width="720"></p>
<p align="center"><sub>Logo by <a href="https://www.reddit.com/user/young_historic">u/young_historic</a></sub></p>

# OuterWilds-Dualsense

**English** | [Русский](README.ru.md)

A DualSense mod for **Outer Wilds** (via [OWML](https://github.com/ow-mods/owml)): real haptic feedback through the controller's audio channels, adaptive triggers, a lightbar that follows the planet you are on, touchpad controls and gyro steering.

> This work is unofficial Fan Content created under permission from the Mobius Digital Fan Content Policy. It includes materials which are the property of Mobius Digital and it is neither approved nor endorsed by Mobius Digital.

## Features

- **Haptic feedback**: almost every sound of the game that you can feel is turned into haptics: the jetpack and the ship's engines, footsteps, jumps and landings, impacts and damage, hazards, water, machinery, doors and lifts, the environments of the planets and much more. The whole Solar System and the **Echoes of the Eye** DLC are covered. Music is left out, except for the signalscope signals.
- **Adaptive triggers**: the jetpack, the ship and some special situations of the game. They switch off in menus, loading, while you sleep or roast a marshmallow, and when the autopilot flies.
- **Lightbar** in the colour of the planet you are on (colours are editable).
- **Touchpad** as input: pan, rotate and zoom the map, pan the ship log map, scroll long texts, press to open an entry in rumor mode. On-screen prompts show PS5 button icons.
- **Gyro steering (experimental, off by default)**: tilt the controller to roll (and pitch and yaw) the ship and the jetpack in zero gravity. Hold **L3** and press **R3** to recalibrate. This feature is experimental: it may feel imprecise, drift or behave unexpectedly, and it only works while the game counts the controller as the active input device.
- **Tuning menu (F1)**: strength, tone and colour of every effect with a **Play** button to feel it. Values are saved to `effects.json`.
- Haptics fall silent while the game is paused and when you play on keyboard and mouse.

## Requirements

- Windows, Outer Wilds (Steam), [OWML](https://github.com/ow-mods/owml) 2.16.2.
- A **DualSense** connected by **USB**. Bluetooth is not supported (the output report for it is not implemented).
- For the haptics the controller must show up in Windows as an audio device ("Speakers (DualSense Wireless Controller)"). The mod finds it by itself.

## Installation

1. Download `Blightyboi.OuterWilds-Dualsense.zip` from the [latest release](../../releases/latest).
2. Unpack it into the `Mods` folder of OWML, so that you get `Mods/Blightyboi.OuterWilds-Dualsense/DualSenseMod.dll` (and `manifest.json` next to it).
3. Start the game through OWML (the OWML Mod Manager or `OWML.Launcher.exe`).

## Settings

Options → Mods → OuterWilds-Dualsense:

| Setting | What it does |
|---|---|
| `adaptiveTriggers` | adaptive triggers on/off |
| `lightbar` | lightbar by planet |
| `audioHaptics` | haptic feedback on/off |
| `gyroMode` | **experimental**; `Off` (default), `Full` (roll, pitch, yaw) or `Roll only` |
| `debugMenu` | allows the F1 tuning menu |
| `debugLog` | verbose log (for bug reports) |

The strength of every effect is set in the F1 menu (see below).

## The F1 menu

Turn on `debugMenu` in the mod settings, then press **F1** in the game. Open a group, change the numbers or pick a colour with the pipette, press **Play** to feel the effect on a running game. Values are stored in `effects.json` in the mod folder; delete the file to go back to the defaults.

## Troubleshooting

- *No haptics, but the triggers work:* the controller's audio device is missing or disabled in Windows (check the Sound settings, the USB cable and the port). The mod reopens the output by itself after a reconnect.
- *Nothing works:* make sure the controller is connected by USB, not Bluetooth. If you use Steam Input for the game, try turning it off for Outer Wilds (it may hide the controller from the mod).
- *Haptics are too weak or too strong:* tune the effect in the F1 menu (the strength of each effect; "Master gain" in the engine sounds group scales all haptics).
- For bug reports please attach the OWML log and, if possible, switch on `debugLog` first.

## Building from source

The repository contains only the mod's own code. It is built against the game's and OWML's assemblies, which are not in the repository (they belong to their owners), so you need Outer Wilds with OWML installed on the same machine.

**What you need**

- Windows and a copy of Outer Wilds (Steam) with [OWML](https://github.com/ow-mods/owml) installed through its installer. The installer puts `OWML.*.dll`, `0Harmony.dll` and the patched `Assembly-CSharp.dll` into `OuterWilds_Data/Managed`; the project references them from there.
- The [.NET SDK](https://dotnet.microsoft.com/download) (6.0 or newer; the project targets .NET Framework 4.8 and gets the reference assemblies from NuGet, so the first build needs an internet connection).
- Git.

**Steps**

1. Clone the repository: `git clone https://github.com/Blightyboi/OuterWilds-Dualsense.git`.
2. Tell the project where the game and the OWML mod folder are. The defaults in `OuterWilds-Dualsense/OuterWilds-Dualsense.csproj` are the author's paths (`GameManaged` and `ModsDir` in the first `PropertyGroup`). Either edit them, or pass your own on the command line (quote paths with spaces):

```bash
dotnet build OuterWilds-Dualsense -c Release -p:GameManaged="C:\Games\Outer Wilds\OuterWilds_Data\Managed" -p:ModsDir="C:\OWML\Mods\Blightyboi.OuterWilds-Dualsense"
```

3. If your paths are already edited in the project file, a plain `dotnet build OuterWilds-Dualsense -c Release` is enough.
4. After a successful build the project copies `DualSenseMod.dll`, `DualSenseMod.pdb`, `manifest.json` and `default-config.json` into `ModsDir`, so you can start the game through OWML right away. The raw output is also in `OuterWilds-Dualsense/bin/Release/net48/`.

**Building a release archive**

Zip a folder named `Blightyboi.OuterWilds-Dualsense` that holds `DualSenseMod.dll`, `manifest.json`, `default-config.json` and `LICENSE`. Do not include your own `config.json` and `effects.json` (they are your local settings). Use forward slashes inside the archive (7-Zip or `zip` do this; on Windows PowerShell 5.1 `Compress-Archive` does not).

**Typical problems**

- *"The type or namespace name ... could not be found"* (`UnityEngine`, `OWML`, `Assembly-CSharp`): `GameManaged` points to the wrong folder, or OWML is not installed in that copy of the game.
- *Restore fails on the first build:* the NuGet package `Microsoft.NETFramework.ReferenceAssemblies` could not be downloaded; check the connection.
- *The game does not load the new build:* make sure `ModsDir` is the folder OWML reads (`<OWML>/Mods/Blightyboi.OuterWilds-Dualsense`), and that the game was restarted.
- If you fork the mod, change `uniqueName` and `author` in `manifest.json`, so that it does not clash with the original.

## License

[MIT](LICENSE). Outer Wilds is a trademark of its owners; this project only reads sounds and state from the running game.
