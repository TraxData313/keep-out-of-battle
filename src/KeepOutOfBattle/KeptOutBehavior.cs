using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace KeepOutOfBattle
{
    // Remembers which heroes of the player's party stay out of battle (chosen on the Order of Battle screen).
    // Saved as hero string ids, so removing the mod leaves nothing behind in the save.
    internal sealed class KeptOutBehavior : CampaignBehaviorBase
    {
        public static KeptOutBehavior? Instance { get; private set; }

        /// <summary>Raised after the player changes who is kept out (the button's count listens).</summary>
        public static event Action? Changed;

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
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (!dataStore.IsLoading)
                _keptOutIds = _keptOut.ToList();
            dataStore.SyncData("KeepOutOfBattle_heroIds", ref _keptOutIds);
            if (dataStore.IsLoading)
                _keptOut = new HashSet<string>(_keptOutIds ?? new List<string>());
        }

        // Heroes of the player's own party who could be sent in: everyone but the player.
        internal static List<Hero> PartyHeroes()
        {
            var party = MobileParty.MainParty;
            if (party?.MemberRoster == null)
                return new List<Hero>();
            return party.MemberRoster.GetTroopRoster()
                .Where(e => e.Character != null && e.Character.IsHero && !e.Character.IsPlayerCharacter && e.Character.HeroObject != null)
                .Select(e => e.Character.HeroObject)
                .ToList();
        }

        // The game's own multi-selection popup over these heroes, the kept-out ones pre-ticked. On "Done" the
        // choice is saved for every later battle, then onDone gets the heroes that were ticked.
        internal void ShowPicker(List<Hero> heroes, string description, Action<List<Hero>> onDone)
        {
            bool canPreselect = PopupPreselectPatch.IsActive;
            var elements = heroes.Select(h => new InquiryElement(
                h,
                canPreselect || !IsKeptOut(h) ? h.Name.ToString() : h.Name.ToString() + " (kept out now)",
                new CharacterImageIdentifier(CharacterCode.CreateFrom(h.CharacterObject)),
                true,
                Describe(h))).ToList();

            var data = new MultiSelectionInquiryData(
                "Keep out of battle",
                description + (canPreselect ? "" : "\nTick everyone who should stay out; anyone left unticked will fight."),
                elements,
                isExitShown: true,
                minSelectableOptionCount: 0,
                maxSelectableOptionCount: elements.Count,
                affirmativeText: "Done",
                negativeText: "Cancel",
                affirmativeAction: chosen =>
                {
                    var ticked = chosen.Select(e => e.Identifier).OfType<Hero>().ToList();
                    SetKeptOut(heroes, ticked);
                    onDone(ticked);
                },
                negativeAction: null);

            HeroHealthBarMixin.Mark(elements);
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
            // Also under the portrait as a bar; here too in case that prefab insert failed.
            parts.Add(HeroHealthBarMixin.HealthPercent(hero) + "% health");
            return hero.Name + ": " + string.Join(", ", parts);
        }

        // Only the heroes shown in the popup change; a kept-out hero who is away from the party stays kept out.
        private void SetKeptOut(IEnumerable<Hero> shown, IEnumerable<Hero> chosen)
        {
            foreach (var hero in shown)
                _keptOut.Remove(hero.StringId);
            foreach (var hero in chosen)
                _keptOut.Add(hero.StringId);
            try
            {
                Changed?.Invoke();
            }
            catch (Exception e)
            {
                SubModule.LogOnce("kept-out change notice", e);
            }
        }
    }
}
