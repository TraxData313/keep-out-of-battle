# Uploading to the Steam Workshop

Same uploader and quirks as the sibling mods (see better-skirmisher-separation's `tools\WORKSHOP-UPLOAD.md`):
Bannerlord's own `TaleWorlds.MountAndBlade.SteamWorkshop.exe`, riding the logged-in Steam client.

Item: **3822341932** (created Public 2026-10-18 via `WorkshopCreate.xml` - never run that file again).
Page: https://steamcommunity.com/sharedfiles/filedetails/?id=3822341932

## Updating

```powershell
powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Force
$task = python tools\make_workshop_update.py --notes "vX.Y.Z - what changed."
& "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.SteamWorkshop.exe" $task
```

Run it from a scratch folder. Success = "Uploading done!" in the output, never the exit code.
The script sends `dist$mod`, `STEAM-DESCRIPTION.bbcode` and the change notes (`--visibility` optional).

## By hand on the item page

The uploader cannot: add Harmony (2859188632) and UIExtenderEx (2859222409) under "Add/Remove Required
Items", and the screenshots in `docs` under "Add/edit images & videos".
