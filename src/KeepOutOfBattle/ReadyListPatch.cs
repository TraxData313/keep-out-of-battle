using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.TroopSuppliers;
using TaleWorlds.Library;

namespace KeepOutOfBattle
{
    // Every campaign battle, land or naval, and every auto-resolve draws its fighters from
    // MapEventSide._readyTroopsPriorityList, which the private MapEventSide.MakeReady fills. Wounded heroes are
    // simply never put in it; a kept-out hero is taken out of it the same way, so they are not spawned, not
    // reinforced and not picked as a target in auto-resolve. They stay in the party, healthy, and share its fate
    // after the battle exactly like a wounded hero would.
    internal static class ReadyListPatch
    {
        // How many heroes MakeReady last took out per side, so the troop supplier built on it can count right.
        private static readonly ConditionalWeakTable<MapEventSide, StrongBox<int>> Excluded = new ConditionalWeakTable<MapEventSide, StrongBox<int>>();

        private static AccessTools.FieldRef<PartyGroupTroopSupplier, int>? _initialTroopCount;
        private static Func<PartyGroupTroopSupplier, MapEventSide>? _partyGroup;

        public static void Apply(Harmony harmony)
        {
            var makeReady = AccessTools.Method(typeof(MapEventSide), "MakeReady")
                ?? throw new MissingMethodException("MapEventSide.MakeReady");
            harmony.Patch(makeReady,
                prefix: new HarmonyMethod(typeof(ReadyListPatch), nameof(MakeReadyPrefix)),
                postfix: new HarmonyMethod(typeof(ReadyListPatch), nameof(MakeReadyPostfix)));

            // PartyGroupTroopSupplier remembers the side's full TroopCount as its initial count; without this its
            // NumTroopsNotSupplied would count the kept-out heroes as reinforcements still to come. Optional.
            try
            {
                _initialTroopCount = AccessTools.FieldRefAccess<PartyGroupTroopSupplier, int>("_initialTroopCount");
                var getter = AccessTools.PropertyGetter(typeof(PartyGroupTroopSupplier), "PartyGroup")
                    ?? throw new MissingMethodException("PartyGroupTroopSupplier.PartyGroup");
                _partyGroup = (Func<PartyGroupTroopSupplier, MapEventSide>)Delegate.CreateDelegate(typeof(Func<PartyGroupTroopSupplier, MapEventSide>), getter);
                var ctor = AccessTools.FirstConstructor(typeof(PartyGroupTroopSupplier), c => c.GetParameters().Length > 0)
                    ?? throw new MissingMethodException("PartyGroupTroopSupplier..ctor");
                harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(ReadyListPatch), nameof(SupplierCtorPostfix)));
            }
            catch (Exception e)
            {
                Debug.Print("[KeepOutOfBattle] supplier count fix not applied (cosmetic only): " + e);
            }
        }

        // The troop-supplier model REMOVES every troop it places from priorTroops while MakeReady runs, so the
        // hand-picked troops have to be read before it, here, and handed to the postfix.
        private static void MakeReadyPrefix(FlattenedTroopRoster? priorTroops, out HashSet<CharacterObject>? __state)
        {
            __state = null;
            try
            {
                if (priorTroops != null && KeptOutBehavior.Instance?.AnyKeptOut == true)
                    __state = new HashSet<CharacterObject>(priorTroops.Select(e => e.Troop).Where(t => t != null && t.IsHero));
            }
            catch (Exception e)
            {
                SubModule.LogOnce("MapEventSide.MakeReady prefix", e);
            }
        }

        private static void MakeReadyPostfix(MapEventSide __instance, bool includeHumanPlayers, HashSet<CharacterObject>? __state,
            List<(FlattenedTroopRosterElement, MapEventParty, float)>? ____readyTroopsPriorityList)
        {
            try
            {
                Excluded.Remove(__instance);
                var list = ____readyTroopsPriorityList;
                var behavior = KeptOutBehavior.Instance;
                if (list == null || list.Count == 0 || behavior == null || !behavior.AnyKeptOut)
                    return;

                var mainParty = PartyBase.MainParty;
                if (mainParty == null || !__instance.Parties.Any(p => p.Party == mainParty))
                    return; // also keeps AI-only auto-resolves allocation-free
                var removed = new List<Hero>();
                var foughtAnyway = new List<Hero>();
                var kept = new List<(FlattenedTroopRosterElement, MapEventParty, float)>(list.Count);
                foreach (var entry in list)
                {
                    var troop = entry.Item1.Troop;
                    if (entry.Item2?.Party == mainParty && troop != null && troop.IsHero && !troop.IsPlayerCharacter
                        && behavior.IsKeptOut(troop.HeroObject))
                    {
                        if (__state != null && __state.Contains(troop))
                            foughtAnyway.Add(troop.HeroObject);
                        else
                        {
                            removed.Add(troop.HeroObject);
                            continue;
                        }
                    }
                    kept.Add(entry);
                }
                var record = RecordFor(__instance.MapEvent);
                if (removed.Count == 0)
                {
                    record?.Fought.UnionWith(foughtAnyway.Select(h => h.StringId));
                    return;
                }
                string names = string.Join(", ", removed.Select(h => h.Name.ToString()));
                // Never empty a side: a battle that starts with nobody on it is worse than one hero fighting.
                // This also happens after a lost battle: a healthy kept-out hero keeps the encounter open (the
                // game continues while a side has a healthy hero), so "Send troops" would send them alone.
                // Said once per battle, auto-resolve included, so it is never a surprise.
                if (kept.Count == 0)
                {
                    record?.Fought.UnionWith(removed.Concat(foughtAnyway).Select(h => h.StringId));
                    if (record == null || !record.WarnedAlone)
                    {
                        if (record != null) record.WarnedAlone = true;
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Nobody else can fight, so " + names + " joined the battle after all.", Colors.Yellow));
                    }
                    return;
                }

                list.Clear();
                list.AddRange(kept);
                Excluded.Add(__instance, new StrongBox<int>(removed.Count));
                record?.KeptOut.UnionWith(removed.Select(h => h.StringId));
                record?.Fought.UnionWith(foughtAnyway.Select(h => h.StringId));
                if (includeHumanPlayers)
                    InformationManager.DisplayMessage(new InformationMessage(
                        "Kept out of battle: " + names + ".", Color.FromUint(0xFF9FD3A6)));
            }
            catch (Exception e)
            {
                SubModule.LogOnce("MapEventSide.MakeReady postfix", e);
            }
        }

        // Who this mod held out of one battle, for KeepOutOfBattleApi. A hero who ended up fighting in any
        // round of it (picked by hand, or nobody else left) counts as having fought.
        private sealed class BattleRecord
        {
            public readonly HashSet<string> KeptOut = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> Fought = new HashSet<string>(StringComparer.Ordinal);
            public bool WarnedAlone;
        }

        private static readonly ConditionalWeakTable<MapEvent, BattleRecord> Battles = new ConditionalWeakTable<MapEvent, BattleRecord>();

        private static BattleRecord? RecordFor(MapEvent? mapEvent) => mapEvent == null ? null : Battles.GetOrCreateValue(mapEvent);

        // A hero taken off the field from the Order of Battle screen.
        public static void MarkKeptOut(MapEvent? mapEvent, string heroId)
        {
            var record = RecordFor(mapEvent);
            if (record == null)
                return;
            record.KeptOut.Add(heroId);
            record.Fought.Remove(heroId);
        }

        public static bool WasKeptOut(MapEvent? mapEvent, string? heroId)
        {
            if (mapEvent == null || string.IsNullOrEmpty(heroId) || !Battles.TryGetValue(mapEvent, out var record))
                return false;
            return record.KeptOut.Contains(heroId!) && !record.Fought.Contains(heroId!);
        }

        // Hideout, lord's hall and naval raid battles pass the troops the player picked on a selection screen
        // (priorTroops). A hero picked there by hand was chosen on purpose and goes in.

        private static void SupplierCtorPostfix(PartyGroupTroopSupplier __instance)
        {
            try
            {
                if (_initialTroopCount == null || _partyGroup == null)
                    return;
                var side = _partyGroup(__instance);
                if (side != null && Excluded.TryGetValue(side, out var box))
                    _initialTroopCount(__instance) -= box.Value;
            }
            catch (Exception e)
            {
                SubModule.LogOnce("PartyGroupTroopSupplier constructor postfix", e);
            }
        }
    }
}
