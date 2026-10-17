using System;
using System.Collections.Generic;
using System.Linq;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using Bannerlord.UIExtenderEx.ViewModels;
using HarmonyLib;
using SandBox.ViewModelCollection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ViewModelCollection.OrderOfBattle;

namespace KeepOutOfBattle
{
    // A "Keep out of battle" button under the hero pool of the campaign's Order of Battle screen (land battles;
    // the naval one is a different screen).
    [PrefabExtension("OrderOfBattle", "descendant::ListPanel[@Id='CaptainPool']")]
    public sealed class OrderOfBattleButtonPrefab : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        [PrefabExtensionText]
        public string Content => @"
<ButtonWidget Id=""KeepOutOfBattleButton"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""210"" SuggestedHeight=""36"" HorizontalAlignment=""Center"" MarginTop=""8"" Brush=""ButtonBrush2"" Command.Click=""ExecuteKeepOutOfBattle"" IsVisible=""@IsKeepOutOfBattleVisible"" UpdateChildrenStates=""true"">
  <Children>
    <TextWidget DoNotAcceptEvents=""true"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Brush=""Popup.Button.Text"" Brush.FontSize=""16"" Text=""@KeepOutOfBattleText"" />
    <HintWidget DataSource=""{KeepOutOfBattleHint}"" DoNotAcceptEvents=""true"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Command.HoverBegin=""ExecuteBeginHint"" Command.HoverEnd=""ExecuteEndHint"" />
  </Children>
</ButtonWidget>";
    }

    [ViewModelMixin]
    public sealed class OrderOfBattleButtonMixin : BaseViewModelMixin<SPOrderOfBattleVM>
    {
        public OrderOfBattleButtonMixin(SPOrderOfBattleVM vm) : base(vm)
        {
            OrderOfBattleKeepOut.Current = new WeakReference<OrderOfBattleVM>(vm);
            KeptOutBehavior.Changed += OnKeptOutChanged;
        }

        public override void OnFinalize()
        {
            KeptOutBehavior.Changed -= OnKeptOutChanged;
            base.OnFinalize();
        }

        private void OnKeptOutChanged() => OnPropertyChanged(nameof(KeepOutOfBattleText));

        [DataSourceProperty] public bool IsKeepOutOfBattleVisible => KeptOutBehavior.Instance != null && MapEvent.PlayerMapEvent != null;

        // "Keep out of battle (2)" while anyone of the party is kept out, so withheld heroes are never forgotten.
        [DataSourceProperty]
        public string KeepOutOfBattleText
        {
            get
            {
                var behavior = KeptOutBehavior.Instance;
                int count = behavior == null ? 0 : KeptOutBehavior.PartyHeroes().Count(behavior.IsKeptOut);
                return count == 0 ? "Keep out of battle" : "Keep out of battle (" + count + ")";
            }
        }

        [DataSourceProperty] public HintViewModel KeepOutOfBattleHint { get; } = new HintViewModel(new TextObject("{=!}"
            + "Choose companions, your surgeon or family who should stay out of the fighting. They step out of the line now "
            + "and leave the field, unhurt, when you press Ready, and stay out of every later battle until you untick them."));

        [DataSourceMethod]
        public void ExecuteKeepOutOfBattle()
        {
            try
            {
                if (ViewModel != null)
                    OrderOfBattleKeepOut.Open(ViewModel);
            }
            catch (Exception e)
            {
                SubModule.Warn("could not open the list: " + e);
            }
        }
    }

    // The Order of Battle side: the picker behind the button, and keeping the screen's hero lists in step with who
    // is hidden. Ticking a hero only HIDES them (off the hero lists, invisible on the field), so unticking before
    // "Ready" puts them back; KeepOutMissionLogic takes them off the field once deployment is over.
    internal static class OrderOfBattleKeepOut
    {
        public static WeakReference<OrderOfBattleVM>? Current;

        private static readonly AccessTools.FieldRef<OrderOfBattleVM, List<OrderOfBattleHeroItemVM>> AllHeroes =
            AccessTools.FieldRefAccess<OrderOfBattleVM, List<OrderOfBattleHeroItemVM>>("_allHeroes");
        private static readonly System.Reflection.MethodInfo? ClearHeroItemSelection =
            AccessTools.Method(typeof(OrderOfBattleVM), "ClearHeroItemSelection");
        private static readonly System.Reflection.MethodInfo? ClearHeroAssignment =
            AccessTools.Method(typeof(OrderOfBattleVM), "ClearHeroAssignment");

        public static OrderOfBattleVM? CurrentVm => Current != null && Current.TryGetTarget(out var vm) ? vm : null;

        public static void Open(OrderOfBattleVM vm)
        {
            var behavior = KeptOutBehavior.Instance;
            var mission = Mission.Current;
            var logic = mission?.GetMissionBehavior<KeepOutMissionLogic>();
            if (behavior == null || mission?.PlayerTeam == null || logic == null)
                return;

            var onField = KeepOutMissionLogic.PartyHeroAgents(mission);
            // Everyone on the field (the hidden ones too), then those kept out who are not here at all, shown ticked.
            var heroes = onField.Keys.ToList();
            heroes.AddRange(KeptOutBehavior.PartyHeroes().Where(h => behavior.IsKeptOut(h) && !onField.ContainsKey(h)));
            if (heroes.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage("No one from your party besides you is in this battle."));
                return;
            }

            behavior.ShowPicker(heroes,
                "Ticked heroes step out of the line now and leave the field, unhurt, when you press Ready; untick them before "
                + "then to bring them back. They stay out of every later battle until you untick them, and still share your "
                + "party's fate if the battle is lost.",
                ticked =>
                {
                    var hidden = new List<Hero>();
                    var restored = new List<Hero>();
                    foreach (var pair in onField)
                    {
                        bool wantOut = ticked.Contains(pair.Key);
                        if (wantOut && !logic.IsPending(pair.Value))
                        {
                            logic.Hide(pair.Value, pair.Key, RemoveFromLists(vm, pair.Value));
                            hidden.Add(pair.Key);
                        }
                        else if (!wantOut && logic.IsPending(pair.Value))
                        {
                            RestoreToLists(vm, logic.Restore(pair.Value));
                            restored.Add(pair.Key);
                        }
                    }
                    if (hidden.Count > 0)
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Kept out of battle: " + Names(hidden) + ".", Color.FromUint(0xFF9FD3A6)));
                    if (restored.Count > 0)
                        InformationManager.DisplayMessage(new InformationMessage(Names(restored) + " will fight in this battle."));

                    var backLater = heroes.Where(h => !onField.ContainsKey(h) && !ticked.Contains(h)).ToList();
                    if (backLater.Count > 0)
                        InformationManager.DisplayMessage(new InformationMessage(Names(backLater) + " will fight again from the next battle."));
                });
        }

        public static string Names(IEnumerable<Hero> heroes) => string.Join(", ", heroes.Select(h => h.Name.ToString()));

        // The Order of Battle holds the agent in a hero item (in the pool, or as a captain / hero troop of a
        // formation). Out of every list, so it can't be assigned while hidden; returned to put it back. Null, and
        // nothing touched, while the screen has no item for the agent (not built yet).
        public static OrderOfBattleHeroItemVM? RemoveFromLists(OrderOfBattleVM vm, Agent agent)
        {
            try
            {
                var items = AllHeroes(vm);
                var item = items?.FirstOrDefault(h => h.Agent == agent);
                if (item == null)
                    return null;
                ClearHeroItemSelection?.Invoke(vm, null);
                ClearHeroAssignment?.Invoke(vm, new object[] { item });
                vm.UnassignedHeroes?.Remove(item);
                items!.Remove(item);
                if (vm.LastSelectedHeroItem == item)
                    vm.LastSelectedHeroItem = null;
                return item;
            }
            catch (Exception e)
            {
                SubModule.LogOnce("Order of Battle hero list update", e);
                return null;
            }
        }

        private static void RestoreToLists(OrderOfBattleVM vm, OrderOfBattleHeroItemVM? item)
        {
            if (item == null)
                return;
            try
            {
                var items = AllHeroes(vm);
                if (!items.Contains(item))
                    items.Add(item);
                if (vm.UnassignedHeroes != null && !vm.UnassignedHeroes.Contains(item))
                    vm.UnassignedHeroes.Add(item);
            }
            catch (Exception e)
            {
                SubModule.LogOnce("Order of Battle hero list restore", e);
            }
        }
    }

    // Every campaign battle: the kept-out heroes of the player's party spawn as usual (a party hero who was never
    // spawned crashed the game a few seconds into deployment), are hidden as soon as they stand on the field, and
    // leave it - an ordered withdrawal, unhurt - once deployment is fully over. The Order of Battle button hides
    // and restores through the same list.
    internal sealed class KeepOutMissionLogic : MissionLogic
    {
        // Ticks to wait after deployment ends before anyone leaves: the mission's OnAfterDeploymentFinished walks
        // Mission.Agents and sets up every agent's stats and equipment in native code; an agent faded out in that
        // window is still in the list but gone in the engine.
        private const int TicksAfterDeployment = 30;
        private const float ScanInterval = 0.25f;

        private readonly Dictionary<Agent, (Hero Hero, OrderOfBattleHeroItemVM? Item)> _pending =
            new Dictionary<Agent, (Hero Hero, OrderOfBattleHeroItemVM? Item)>();
        private int _ticksSinceDeployment = -1;
        private float _scanTimer;

        public bool IsPending(Agent agent) => _pending.ContainsKey(agent);

        public void Hide(Agent agent, Hero hero, OrderOfBattleHeroItemVM? item)
        {
            _pending[agent] = (hero, item);
            SetVisible(agent, false);
        }

        public OrderOfBattleHeroItemVM? Restore(Agent agent)
        {
            if (!_pending.TryGetValue(agent, out var entry))
                return null;
            _pending.Remove(agent);
            SetVisible(agent, true);
            return entry.Item;
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            try
            {
                _scanTimer -= dt;
                if (_scanTimer <= 0f)
                {
                    _scanTimer = ScanInterval;
                    HideKeptOutHeroes();
                }

                if (!Mission.IsDeploymentFinished)
                {
                    TakePendingOffTheScreenLists();
                    return;
                }
                if (_ticksSinceDeployment < TicksAfterDeployment)
                {
                    _ticksSinceDeployment++;
                    return;
                }
                if (_pending.Count > 0)
                    RemovePending();
            }
            catch (Exception e)
            {
                SubModule.LogOnce("KeepOutMissionLogic tick", e);
            }
        }

        // The player's own party's heroes standing in this battle, the player excepted.
        public static Dictionary<Hero, Agent> PartyHeroAgents(Mission mission)
        {
            var result = new Dictionary<Hero, Agent>();
            if (mission.PlayerTeam == null)
                return result;
            foreach (var agent in mission.PlayerTeam.ActiveAgents)
            {
                if (agent == null || !agent.IsHuman || agent.IsMainAgent || !agent.IsActive())
                    continue;
                if (!(agent.Character is CharacterObject character) || !character.IsHero || character.IsPlayerCharacter)
                    continue;
                if (agent.Origin?.BattleCombatant != PartyBase.MainParty || character.HeroObject == null)
                    continue;
                result[character.HeroObject] = agent;
            }
            return result;
        }

        // Kept-out heroes who just stood up on the field (at the start, or later as reinforcements) step out.
        private void HideKeptOutHeroes()
        {
            var behavior = KeptOutBehavior.Instance;
            if (behavior == null || !behavior.AnyKeptOut || Mission.PlayerTeam == null)
                return;
            // Hideouts are fought by a hand-picked band: whoever was picked was picked on purpose.
            if (MapEvent.PlayerMapEvent?.IsHideoutBattle == true)
                return;

            var newcomers = PartyHeroAgents(Mission)
                .Where(p => behavior.IsKeptOut(p.Key) && !IsPending(p.Value))
                .ToList();
            if (newcomers.Count == 0)
                return;
            // Never empty the side: if nobody else of ours stands here, they fight after all.
            int others = Mission.PlayerTeam.ActiveAgents.Count(a => a != null && a.IsHuman && a.IsActive() && !IsPending(a)
                && !newcomers.Any(n => n.Value == a));
            if (others == 0)
                return;

            var vm = Mission.IsDeploymentFinished ? null : OrderOfBattleKeepOut.CurrentVm;
            foreach (var pair in newcomers)
                Hide(pair.Value, pair.Key, vm == null ? null : OrderOfBattleKeepOut.RemoveFromLists(vm, pair.Value));
            InformationManager.DisplayMessage(new InformationMessage(
                "Kept out of battle: " + OrderOfBattleKeepOut.Names(newcomers.Select(p => p.Key)) + ".", Color.FromUint(0xFF9FD3A6)));
        }

        // The Order of Battle builds its hero list a moment after the agents spawn; a hero hidden before that is
        // taken off it as soon as it exists.
        private void TakePendingOffTheScreenLists()
        {
            var vm = OrderOfBattleKeepOut.CurrentVm;
            if (vm == null || _pending.Count == 0)
                return;
            foreach (var agent in _pending.Where(p => p.Value.Item == null).Select(p => p.Key).ToList())
            {
                var item = OrderOfBattleKeepOut.RemoveFromLists(vm, agent);
                if (item != null)
                    _pending[agent] = (_pending[agent].Hero, item);
            }
        }

        private void RemovePending()
        {
            foreach (var pair in _pending.ToList())
            {
                try
                {
                    TakeOffTheField(pair.Key, pair.Value.Hero);
                }
                catch (Exception e)
                {
                    SubModule.LogOnce("taking a hero off the field", e);
                }
            }
            _pending.Clear();
        }

        // Booked first and explicitly as an ordered withdrawal (not a rout): back in the party, unhurt, and never
        // sent in again as a reinforcement. The removal the fade-out reports afterwards finds the origin settled.
        private static void TakeOffTheField(Agent agent, Hero hero)
        {
            if (!agent.IsActive())
                return;
            if (agent.Formation?.Captain == agent)
                agent.Formation.Captain = null;
            agent.Origin?.SetRouted(isOrderRetreat: true);
            // Visuals back on first, so the engine fades out an agent in the state it always sees one in.
            SetVisible(agent, true);
            agent.FadeOut(hideInstantly: true, hideMount: true);
            ReadyListPatch.MarkKeptOut(MapEvent.PlayerMapEvent, hero.StringId);
        }

        private static void SetVisible(Agent agent, bool visible)
        {
            try
            {
                agent.AgentVisuals?.SetVisible(visible);
                agent.MountAgent?.AgentVisuals?.SetVisible(visible);
            }
            catch (Exception e)
            {
                SubModule.LogOnce("hiding a hero on the field", e);
            }
        }
    }
}
