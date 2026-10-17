using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Library;

namespace KeepOutOfBattle
{
    // AUTO-RESOLVE ONLY. Every simulated battle draws the troops it can wound from MapEventSide._readyTroopsPriorityList,
    // which the private MapEventSide.MakeReady fills. Wounded heroes are never put in it; a kept-out hero is taken
    // out of it the same way, so the simulation can't touch them.
    //
    // Fought battles (missions) are NOT filtered here any more: a party hero who was never spawned crashed the game
    // natively a few seconds into deployment, every time (playtest, 2026-10-17, two crashes in a row). In a mission
    // the kept-out heroes spawn as usual and KeepOutMissionLogic hides them and takes them off the field once
    // deployment is over - the path that has held up in play.
    internal static class ReadyListPatch
    {
        public static void Apply(Harmony harmony)
        {
            var makeReady = AccessTools.Method(typeof(MapEventSide), "MakeReady")
                ?? throw new MissingMethodException("MapEventSide.MakeReady");
            harmony.Patch(makeReady, postfix: new HarmonyMethod(typeof(ReadyListPatch), nameof(MakeReadyPostfix)));
        }

        private static void MakeReadyPostfix(MapEventSide __instance, bool includeHumanPlayers,
            List<(FlattenedTroopRosterElement, MapEventParty, float)>? ____readyTroopsPriorityList)
        {
            try
            {
                if (includeHumanPlayers)
                    return; // a mission: see KeepOutMissionLogic
                var list = ____readyTroopsPriorityList;
                var behavior = KeptOutBehavior.Instance;
                if (list == null || list.Count == 0 || behavior == null || !behavior.AnyKeptOut)
                    return;

                var mainParty = PartyBase.MainParty;
                if (mainParty == null || !__instance.Parties.Any(p => p.Party == mainParty))
                    return; // also keeps AI-only auto-resolves allocation-free
                var removed = new List<Hero>();
                var kept = new List<(FlattenedTroopRosterElement, MapEventParty, float)>(list.Count);
                foreach (var entry in list)
                {
                    var troop = entry.Item1.Troop;
                    if (entry.Item2?.Party == mainParty && troop != null && troop.IsHero && !troop.IsPlayerCharacter
                        && behavior.IsKeptOut(troop.HeroObject))
                    {
                        removed.Add(troop.HeroObject);
                        continue;
                    }
                    kept.Add(entry);
                }
                if (removed.Count == 0)
                    return;
                var record = RecordFor(__instance.MapEvent);
                // Never empty a side: a simulation with nobody on it is worse than one hero fighting. This also
                // happens after a lost battle: a healthy kept-out hero keeps the encounter open (the game continues
                // while a side has a healthy hero), so "Send troops" would send them alone. Said once per battle.
                if (kept.Count == 0)
                {
                    record?.Fought.UnionWith(removed.Select(h => h.StringId));
                    if (record == null || !record.WarnedAlone)
                    {
                        if (record != null) record.WarnedAlone = true;
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Nobody else can fight, so " + string.Join(", ", removed.Select(h => h.Name.ToString()))
                            + " joined the battle after all.", Colors.Yellow));
                    }
                    return;
                }

                list.Clear();
                list.AddRange(kept);
                record?.KeptOut.UnionWith(removed.Select(h => h.StringId));
            }
            catch (Exception e)
            {
                SubModule.LogOnce("MapEventSide.MakeReady postfix", e);
            }
        }

        // Who this mod held out of one battle, for KeepOutOfBattleApi. A hero who ended up fighting in any
        // round of it (nobody else left) counts as having fought.
        private sealed class BattleRecord
        {
            public readonly HashSet<string> KeptOut = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> Fought = new HashSet<string>(StringComparer.Ordinal);
            public bool WarnedAlone;
        }

        private static readonly ConditionalWeakTable<MapEvent, BattleRecord> Battles = new ConditionalWeakTable<MapEvent, BattleRecord>();

        private static BattleRecord? RecordFor(MapEvent? mapEvent) => mapEvent == null ? null : Battles.GetOrCreateValue(mapEvent);

        // A hero taken off the field in a fought battle.
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
    }
}
