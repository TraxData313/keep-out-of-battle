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
    // the naval one is a different screen and keeps the encounter-menu option only).
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
        }

        [DataSourceProperty] public bool IsKeepOutOfBattleVisible => KeptOutBehavior.Instance != null && MapEvent.PlayerMapEvent != null;
        [DataSourceProperty] public string KeepOutOfBattleText => "Keep out of battle";

        [DataSourceProperty] public HintViewModel KeepOutOfBattleHint { get; } = new HintViewModel(new TextObject("{=!}"
            + "Choose companions, your surgeon or family who should stay out of the fighting. They leave the field now, unhurt, "
            + "and stay out of every later battle until you untick them."));

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

    internal static class OrderOfBattleKeepOut
    {
        private static readonly AccessTools.FieldRef<OrderOfBattleVM, List<OrderOfBattleHeroItemVM>> AllHeroes =
            AccessTools.FieldRefAccess<OrderOfBattleVM, List<OrderOfBattleHeroItemVM>>("_allHeroes");
        private static readonly System.Reflection.MethodInfo? ClearHeroItemSelection =
            AccessTools.Method(typeof(OrderOfBattleVM), "ClearHeroItemSelection");
        private static readonly System.Reflection.MethodInfo? ClearHeroAssignment =
            AccessTools.Method(typeof(OrderOfBattleVM), "ClearHeroAssignment");

        public static void Open(OrderOfBattleVM vm)
        {
            var behavior = KeptOutBehavior.Instance;
            var mission = Mission.Current;
            if (behavior == null || mission?.PlayerTeam == null)
                return;

            var onField = PartyHeroAgents(mission);
            // Everyone on the field, then those already kept out of it, who are shown ticked.
            var heroes = onField.Keys.ToList();
            heroes.AddRange(KeptOutBehavior.PartyHeroes().Where(h => behavior.IsKeptOut(h) && !onField.ContainsKey(h)));
            if (heroes.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage("No one from your party besides you is in this battle."));
                return;
            }

            behavior.ShowPicker(heroes,
                "Ticked heroes leave the field now, unhurt, and stay out of every later battle until you untick them. "
                + "They still share your party's fate if the battle is lost.",
                ticked =>
                {
                    var leaving = ticked.Where(onField.ContainsKey).ToList();
                    foreach (var hero in leaving)
                        TakeOffTheField(vm, onField[hero], hero);
                    if (leaving.Count > 0)
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Kept out of battle: " + string.Join(", ", leaving.Select(h => h.Name.ToString())) + ".", Color.FromUint(0xFF9FD3A6)));

                    var backLater = heroes.Where(h => !onField.ContainsKey(h) && !ticked.Contains(h)).ToList();
                    if (backLater.Count > 0)
                        InformationManager.DisplayMessage(new InformationMessage(
                            string.Join(", ", backLater.Select(h => h.Name.ToString())) + " will fight again from the next battle."));
                });
        }

        // The player's own party's heroes standing in this battle, the player excepted.
        private static Dictionary<Hero, Agent> PartyHeroAgents(Mission mission)
        {
            var result = new Dictionary<Hero, Agent>();
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

        // Out of the Order of Battle's lists first (it holds the agent in its hero items), then off the field. The
        // mission books an agent that leaves this way like one that withdrew in good order: back in the party, unhurt,
        // and never sent in again as a reinforcement.
        private static void TakeOffTheField(OrderOfBattleVM vm, Agent agent, Hero hero)
        {
            try
            {
                ClearHeroItemSelection?.Invoke(vm, null);
                var items = AllHeroes(vm);
                var item = items.FirstOrDefault(h => h.Agent == agent);
                if (item != null)
                {
                    ClearHeroAssignment?.Invoke(vm, new object[] { item });
                    vm.UnassignedHeroes?.Remove(item);
                    items.Remove(item);
                    if (vm.LastSelectedHeroItem == item)
                        vm.LastSelectedHeroItem = null;
                }
            }
            catch (Exception e)
            {
                SubModule.LogOnce("Order of Battle hero list update", e);
            }

            if (agent.Formation?.Captain == agent)
                agent.Formation.Captain = null;
            // Booked first and explicitly as an ordered withdrawal (not a rout): the hero goes back to the party
            // whole. The removal the fade-out reports afterwards finds the origin already settled.
            agent.Origin?.SetRouted(isOrderRetreat: true);
            agent.FadeOut(hideInstantly: true, hideMount: true);
            ReadyListPatch.MarkKeptOut(MapEvent.PlayerMapEvent, hero.StringId);
        }
    }
}
