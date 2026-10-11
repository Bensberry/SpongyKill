# Spongykill Ops

A cartoon, undersea tactical shooter in the spirit of Critical Ops: 5 v 5 team deathmatch in **Coral Plaza**, a Bikini Bottom-style downtown with pineapple houses, a bus depot, a burger joint and a lighthouse pier. Play against bots offline, or online against other players on dedicated servers hosted on Edgegap. PC and Android builds can play together.

Made in Unity 6 (URP) with Netcode for GameObjects. All art, sound and code are original: the models are built from primitives and the sounds are generated.

## Playing

From the main menu:

- **Play vs Bots.** A private match against Easy, Normal or Hard bots.
- **Quick Match (Online).** Finds an online match through Edgegap's matchmaker.
- **Join by Address / Host LAN Game.** Play on your local network or join a known server.
- **Settings.** Sensitivity, gyro, graphics, particles, crosshair, enemy outline, minimap and touch layout.

### Match rules

- Team deathmatch: the first team to 50 kills wins, or whichever team leads when the 8 minutes run out.
- In Play vs Bots, bots fill the empty slots. Online matches are players only.
- You pick your weapons before deploying. Press B, or use the button on the death screen, to change them for your next life.
- Aiming works like CS: only the sniper (Harpoon Longshot) scopes, and every other gun fires from the hip.
- You get a short spawn protection after each spawn. It ends when you fire.

### PC controls

| Action | Key |
|---|---|
| Move / sprint / crouch / jump | WASD / Shift / Ctrl or C / Space |
| Fire / scope (sniper) | Left mouse / right mouse (toggle) |
| Reload / inspect | R / F |
| Weapons | 1-3, Q (last weapon), mouse wheel |
| Loadout / scoreboard / menu | B / Tab / Esc |

### Phone and tablet controls

- Use the left stick to move. Push it forward past the rim to sprint.
- Drag anywhere on the right side to look.
- The big **FIRE** button also aims while you drag it. There's a second fire button on the left.
- Buttons: **JUMP**, **CROUCH** (toggle), **RELOAD**, **SCOPE** (sniper only) and **USE**. Tap a weapon on the bar at the bottom to switch to it.
- Gyro aiming can be turned on, or set to work only while scoped, in Settings > Controls.
- Every button can be moved and resized: Settings > Controls > Touch button layout > Customise.

## Builds

Everything is under **Spongykill** in the Unity menu bar:

- **Build > Windows (Client + Local Server).** Writes `Builds/Windows/SpongykillOps.exe`.
- **Build > Android APK.** Writes `Builds/Android/SpongykillOps.apk` (arm64, Android 8.0+). Install it with `adb install` or copy it to the device.
- **Build > Linux Dedicated Server (Edgegap).** Needs Unity's Linux server modules.
- **Build > Run Local Server From Windows Build.** Starts a headless server on UDP 7777.
- **Online > Build Main Menu And Network Setup** and **Maps > Build Coral Plaza** regenerate the scenes.

The Edgegap matchmaker configuration is in [Docs/edgegap-matchmaker.json](Docs/edgegap-matchmaker.json). The matchmaker URL and token go in `Assets/_Spongykill/Resources/OnlineConfig.asset`.
