using TaleWorlds.CampaignSystem.MapEvents;

namespace KeepOutOfBattle
{
    /// <summary>
    /// For other mods, found by reflection so neither side needs a reference: look up the type
    /// "KeepOutOfBattle.KeepOutOfBattleApi" in the loaded assemblies and bind the static methods.
    /// CONTRACT: Immersive AI binds these by name and signature; never rename or change them without
    /// updating its KeepOutOfBattleBridge.
    /// </summary>
    public static class KeepOutOfBattleApi
    {
        /// <summary>True when the hero was actually held out of this battle (mission or auto-resolve):
        /// ticked AND taken out of the fighters. False when they fought after all (picked by hand on a
        /// selection screen, or nobody else was left), and for any battle this mod never touched.</summary>
        public static bool WasKeptOut(MapEvent mapEvent, string heroId) => ReadyListPatch.WasKeptOut(mapEvent, heroId);

        /// <summary>True when the player has currently ticked this hero to stay out of battles.</summary>
        public static bool IsKeptOut(string heroId)
        {
            var behavior = KeptOutBehavior.Instance;
            return behavior != null && behavior.IsKeptOutId(heroId);
        }
    }
}
