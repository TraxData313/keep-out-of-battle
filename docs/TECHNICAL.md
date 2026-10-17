# Keep Out of Battle — technical notes

For Bannerlord v1.4.8, with or without the War Sails DLC.

## Using it

Every pre-battle menu (field encounter, joining a battle, army battles, siege strategies, naval
encounters) gets an option **Choose who stays out of battle**. It shows only when your party has heroes
other than you. It opens a list of those heroes with portraits; the tooltip on each says if they are your
surgeon, quartermaster, scout, engineer or spouse. Ticked heroes stay out of every battle until unticked.
The option's label shows how many are kept out, and its tooltip names them.

At the start of a battle a green line in the log names who was kept out.

## What "kept out" means

- **Field, siege and naval battles:** the hero is not spawned and not brought in as a reinforcement.
- **Auto-resolve ("Send troops"):** the hero is not among the troops the simulation can wound.
- **Hideouts, lord's hall, naval raids:** these battles have their own troop selection screen. A hero you
  pick there by hand goes in anyway; one you don't pick stays out as usual.
- **Nobody else left:** if everyone who could fight is down and only kept-out heroes remain, they fight
  after all (a battle with nobody on your side can't start). The log says so.
- **Losing:** a kept-out hero is still in your party. If the battle is lost they are captured with it,
  exactly like a wounded companion. Note the game keeps an encounter going while your side has any
  healthy hero, so after a lost fight you may land back in the encounter menu: choosing "Send troops"
  then would send the kept-out hero in alone (the log warns you). Surrender or leave instead if you'd
  rather they not fight.
- **Immersive AI:** when it is installed, a kept-out hero's memory of the battle says they were kept back
  at your word and watched from behind the lines (or from a ship at sea), instead of "none fell to my
  hand". It asks this mod through `KeepOutOfBattleApi.WasKeptOut(MapEvent, heroId)`, found by reflection.
- Only heroes in your own party are affected; the player always fights.

Safe to add mid-campaign. Removing it is also safe: the save only holds a list of hero ids under the
behavior's own key, which the game ignores without the mod.

## How it works

Every campaign battle, land or naval, and every auto-resolve draws its fighters from
`MapEventSide._readyTroopsPriorityList`, filled by the private `MapEventSide.MakeReady`. Wounded heroes are
never put in that list. A Harmony postfix on `MakeReady` takes the kept-out heroes of the main party out
of it the same way, so the spawn logic (`PartyGroupTroopSupplier`, used by both the land and the War Sails
spawn logic) never sees them.

The game's troop model empties the hand-picked `priorTroops` list while `MakeReady` runs, so a prefix
reads it first and hands it to the postfix; heroes in it are never taken out.

A postfix on the `PartyGroupTroopSupplier` constructor lowers its initial troop count by the number taken
out, so "troops not supplied yet" stays correct.

The popup is the game's own multi-selection inquiry. A postfix on `MultiSelectionQueryPopUpVM.SetData`
ticks the heroes already kept out when *our* popup opens (matched by reference). If that patch fails, the
popup still works: kept-out heroes are marked "(kept out now)" in their name instead.

## Build

Needs the .NET SDK. Paths are in `Directory.Build.props`; put yours in a git-ignored
`Directory.Build.props.user`.

```
powershell -ExecutionPolicy Bypass -File tools\deploy.ps1
```

installs the dev build as `Modules\KeepOutOfBattle.Dev` ("Keep Out of Battle (dev)" in the launcher).

```
powershell -ExecutionPolicy Bypass -File tools\package.ps1
```

builds the release layout under `dist\KeepOutOfBattle` and the zip for a GitHub release.
