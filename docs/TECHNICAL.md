# Keep Out of Battle — technical notes

For Bannerlord v1.4.8, with or without the War Sails DLC.

## Using it

On the Order of Battle screen of a land battle, a **Keep out of battle** button sits under the hero pool at
the top. It opens a list of the heroes in your party with portraits; the tooltip on each says if they are
your surgeon, quartermaster, scout, engineer or spouse. Heroes already kept out are shown ticked.

Ticking a hero there makes them step out of the line at once (hidden and taken off the hero lists); they
leave the field, unhurt, when you press Ready. Untick them before Ready and they are back in the line and the
hero pool. The choice is saved with your campaign: in later battles ticked heroes are never spawned at all.
Untick them on any Order of Battle screen and they fight again from the next battle.

At the start of a battle a green line in the log names who was kept out.

Naval battles (War Sails) use a different Order of Battle screen with no button, but whoever you kept out on
land stays out at sea too.

## What "kept out" means

- **Field, siege and naval battles:** the hero steps out of the line as soon as they spawn (also as a reinforcement) and leaves the field, unhurt, once deployment is over.
- **Auto-resolve ("Send troops"):** the hero is not among the troops the simulation can wound.
- **Hideouts:** you pick the band yourself there, so a hero you pick goes in.
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

**Fought battles.** Kept-out heroes spawn as usual; `KeepOutMissionLogic` hides them (agent and mount,
`AgentVisuals.SetVisible(false)`) as soon as they stand on the field, takes their item off the Order of Battle's
hero lists, and about half a second after deployment has finished books each one as an ordered withdrawal
(`SetRouted(isOrderRetreat: true)`: back to the party unhurt, never sent in again as a reinforcement) and fades
them out. Reinforcements are watched the same way. Two lessons from playtesting are behind this shape:

- A party hero kept out of the spawn list altogether (never spawned) crashed the game natively a few seconds
  into deployment, every time.
- Fading an agent out inside `OnDeploymentFinished` is too early: right after it the mission's
  `OnAfterDeploymentFinished` walks `Mission.Agents` and sets up every agent in native code.

**Auto-resolve.** A Harmony postfix on the private `MapEventSide.MakeReady` takes the kept-out heroes of the main
party out of `_readyTroopsPriorityList` for simulations only, so the simulation can't wound them.

The Order of Battle button is a UIExtenderEx prefab insert after the `CaptainPool` panel of the
`OrderOfBattle` movie, with a view-model mixin on the campaign's `SPOrderOfBattleVM`. Ticking a hero there
clears their hero item from the Order of Battle's lists (`ClearHeroAssignment`, `_allHeroes`,
`UnassignedHeroes`) and hides the agent and mount (`AgentVisuals.SetVisible(false)`); a `KeepOutMissionLogic`
keeps them pending, and unticking puts the item and the visuals back. At `OnDeploymentFinished` each pending
agent's origin is booked as an ordered withdrawal (`SetRouted(isOrderRetreat: true)`: back to the party
unhurt, never sent in again as a reinforcement), then the agent and its mount are faded out.
The naval Order of Battle is a different screen and has no button; choices made on land apply at sea.

The popup is the game's own multi-selection inquiry. A postfix on `MultiSelectionQueryPopUpVM.SetData`
ticks the heroes already kept out when *our* popup opens (matched by reference). If that patch fails, the
popup still works: kept-out heroes are marked "(kept out now)" in their name instead.

## Build

Needs the .NET SDK, Harmony and UIExtenderEx. Paths are in `Directory.Build.props`; put yours in a git-ignored
`Directory.Build.props.user`.

```
powershell -ExecutionPolicy Bypass -File tools\deploy.ps1
```

installs the dev build as `Modules\KeepOutOfBattle.Dev` ("Keep Out of Battle (dev)" in the launcher).

```
powershell -ExecutionPolicy Bypass -File tools\package.ps1
```

builds the release layout under `dist\KeepOutOfBattle` and the zip for a GitHub release.
