using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace KeepOutOfBattle
{
    // Remembers which heroes of the player's party stay out of battle, and adds the menu option that opens the
    // choice before every fight. Saved as hero string ids, so removing the mod leaves nothing behind in the save.
    internal sealed class KeptOutBehavior : CampaignBehaviorBase
    {
        private const string OptionId = "keep_out_of_battle";

        // The pre-battle menus of land, siege and naval encounters (War Sails uses "encounter" too; its
        // storyline has its own). A menu that does not exist in this game is created empty and never shown.
        private static readonly string[] BattleMenus =
        {
            "encounter",
            "join_encounter",
            "army_encounter",
            "encounter_interrupted",
            "encounter_interrupted_siege_preparations",
            "menu_siege_strategies",
            "join_siege_event",
            "naval_storyline_encounter",
            "naval_storyline_join_encounter",
        };

        public static KeptOutBehavior? Instance { get; private set; }

        private List<string> _keptOutIds = new List<string>();
        private HashSet<string> _keptOut = new HashSet<string>();

        public KeptOutBehavior()
        {
            Instance = this;
        }

        public bool AnyKeptOut => _keptOut.Count > 0;

        public bool IsKeptOut(Hero? hero) => hero != null && _keptOut.Contains(hero.StringId);

        public bool IsKeptOutId(string? heroId) => !string.IsNullOrEmpty(heroId) && _keptOut.Contains(heroId!);

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (!dataStore.IsLoading)
                _keptOutIds = _keptOut.ToList();
            dataStore.SyncData("KeepOutOfBattle_heroIds", ref _keptOutIds);
            if (dataStore.IsLoading)
                _keptOut = new HashSet<string>(_keptOutIds ?? new List<string>());
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            foreach (var menu in BattleMenus)
                starter.AddGameMenuOption(menu, OptionId, "{=!}{KEEP_OUT_OF_BATTLE_TEXT}", OptionCondition, OptionConsequence, index: 1);
        }

        // Heroes of the player's own party who could be sent in: everyone but the player.
        private static List<Hero> PartyHeroes()
        {
            var party = MobileParty.MainParty;
            if (party?.MemberRoster == null)
                return new List<Hero>();
            return party.MemberRoster.GetTroopRoster()
                .Where(e => e.Character != null && e.Character.IsHero && !e.Character.IsPlayerCharacter && e.Character.HeroObject != null)
                .Select(e => e.Character.HeroObject)
                .ToList();
        }

        private bool OptionCondition(MenuCallbackArgs args)
        {
            var heroes = PartyHeroes();
            if (heroes.Count == 0)
                return false;
            args.optionLeaveType = GameMenuOption.LeaveType.Manage;
            int count = heroes.Count(IsKeptOut);
            string text = count == 0
                ? "Choose who stays out of battle"
                : "Choose who stays out of battle (" + count + " kept out)";
            MBTextManager.SetTextVariable("KEEP_OUT_OF_BATTLE_TEXT", text);
            args.Tooltip = new TextObject("{=!}" + (count == 0
                ? "Pick the companions, surgeon or family in your party who should not fight. Remembered for every battle."
                : "Staying out: " + string.Join(", ", heroes.Where(IsKeptOut).Select(h => h.Name.ToString()))));
            return true;
        }

        private void OptionConsequence(MenuCallbackArgs args)
        {
            var heroes = PartyHeroes();
            bool canPreselect = PopupPreselectPatch.IsActive;
            var elements = heroes.Select(h => new InquiryElement(
                h,
                canPreselect || !IsKeptOut(h) ? h.Name.ToString() : h.Name.ToString() + " (kept out now)",
                new CharacterImageIdentifier(CharacterCode.CreateFrom(h.CharacterObject)),
                true,
                Describe(h))).ToList();

            var data = new MultiSelectionInquiryData(
                "Keep out of battle",
                "Ticked heroes stay behind in this and every later battle, on land and at sea, until you untick them. "
                + "They still share your party's fate if the battle is lost."
                + (canPreselect ? "" : "\nTick everyone who should stay out; anyone left unticked will fight."),
                elements,
                isExitShown: true,
                minSelectableOptionCount: 0,
                maxSelectableOptionCount: elements.Count,
                affirmativeText: "Done",
                negativeText: "Cancel",
                affirmativeAction: chosen =>
                {
                    SetKeptOut(heroes, chosen.Select(e => e.Identifier).OfType<Hero>());
                    Campaign.Current?.CurrentMenuContext?.Refresh();
                },
                negativeAction: null);

            if (canPreselect)
                PopupPreselectPatch.Expect(data, heroes.Where(IsKeptOut).Cast<object>());
            MBInformationManager.ShowMultiSelectionInquiry(data);
        }

        private static string Describe(Hero hero)
        {
            var parts = new List<string>();
            var party = MobileParty.MainParty;
            if (party != null)
            {
                if (party.EffectiveSurgeon == hero) parts.Add("Surgeon");
                if (party.EffectiveQuartermaster == hero) parts.Add("Quartermaster");
                if (party.EffectiveScout == hero) parts.Add("Scout");
                if (party.EffectiveEngineer == hero) parts.Add("Engineer");
            }
            if (Hero.MainHero?.Spouse == hero) parts.Add("Spouse");
            if (hero.IsWounded) parts.Add("wounded");
            return parts.Count == 0 ? hero.Name.ToString() : hero.Name + ": " + string.Join(", ", parts);
        }

        // Only the heroes shown in the popup change; a kept-out hero who is away from the party stays kept out.
        private void SetKeptOut(IEnumerable<Hero> shown, IEnumerable<Hero> chosen)
        {
            foreach (var hero in shown)
                _keptOut.Remove(hero.StringId);
            foreach (var hero in chosen)
                _keptOut.Add(hero.StringId);
        }
    }
}
