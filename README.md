# Sineus Arena Mod Settings Menu

A BepInEx plugin for **Sineus Arena** that injects a customized **Mod Settings** menu directly into the game's settings panel. It parses and manages mod configurations (`.cfg` files) dynamically from the `BepInEx/config` folder.

## Features

- **Seamless Injection**: Injects a "Mod Settings" button mathematically positioned between the "Apply" and "Defaults" buttons in both Lobby settings, Pause settings, and the Main Menu.
- **Dynamic Config Handling**: Automatically scans BepInEx configurations.
- **Active Mod Filtering**: Only displays settings for mods that are currently loaded in the game session (filtered using BepInEx PluginInfo).
- **Responsive Layout**: Designed with a clean, dynamic IMGUI overlay style which automatically wraps descriptions, prevents text overlap, and supports vertical scroll.
- **Zero Input Leakage**: Disables active `GraphicRaycaster` components and captures `Escape` key close events so background settings menus don't receive mouse/keyboard click-throughs.
- **Persistent Pause**: Keeps the game paused in the background when configuring mods in the in-game pause screen.

## Installation

1. Install [BepInEx](https://github.com/BepInEx/BepInEx) (v5 or later) into your *Sineus Arena* directory.
2. Build this project and place the compiled `ModSettingsMenu.dll` inside the `BepInEx/plugins/ModSettingsMenu/` directory.
3. Start the game, open Settings, and click **Mod Settings**!
