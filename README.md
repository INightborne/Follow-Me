# Follow Me

A Dalamud plugin for Final Fantasy XIV that lets you target an overworld NPC and automatically follow it using vnavmesh.

## Requirements

- XIVLauncher / Dalamud
- vnavmesh installed and enabled

## Install

Add this URL in Dalamud:

`/xlsettings` → **Experimental** → **Custom Plugin Repositories**

```
https://raw.githubusercontent.com/INightborne/Follow-Me/main/pluginmaster.json
```

Then open `/xlplugins`, search for **Follow Me**, and install it.

## Commands

- `/followme` — start following your currently targeted NPC, or stop if already following
- `/followme stop` — stop following
- `/followme status` — show current status
- `/followme distance 3` — set follow distance in yalms
- `/followme 3` — shorthand to set distance and start following if stopped

The plugin follows Battle NPCs and Event NPCs by object ID, so changing your target after starting does not change who you are following.

## Notes

This is a third-party plugin and uses vnavmesh IPC for movement. It is not affiliated with Square Enix, XIVLauncher, Dalamud, or the vnavmesh project.
