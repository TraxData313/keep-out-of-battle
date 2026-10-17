using System;
using Bannerlord.UIExtenderEx;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace KeepOutOfBattle
{
    public class SubModule : MBSubModuleBase
    {
        internal const string HarmonyId = "trax.bannerlord.keepoutofbattle";

        private static bool _patched;
        private static UIExtender? _uiExtender;
        private static readonly System.Collections.Generic.HashSet<string> Logged = new System.Collections.Generic.HashSet<string>();

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            try
            {
                _uiExtender = UIExtender.Create("KeepOutOfBattle");
                _uiExtender.Register(typeof(SubModule).Assembly);
                _uiExtender.Enable();
            }
            catch (Exception e)
            {
                // Costs only the Order of Battle button; the encounter-menu option still works.
                Debug.Print("[KeepOutOfBattle] UIExtenderEx setup failed: " + e);
            }
        }

        // Patched on the first campaign start rather than in OnSubModuleLoad, so nothing the patched methods
        // touch is JIT-compiled (and its static constructors run) before a campaign exists.
        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            if (!(game.GameType is Campaign) || !(gameStarterObject is CampaignGameStarter starter))
                return;
            starter.AddBehavior(new KeptOutBehavior());
            if (_patched)
                return;
            _patched = true;
            var harmony = new Harmony(HarmonyId);
            try
            {
                ReadyListPatch.Apply(harmony);
            }
            catch (Exception e)
            {
                Warn("patching the battle troop list failed, everyone fights as in vanilla: " + e);
            }
            try
            {
                PopupPreselectPatch.Apply(harmony);
            }
            catch (Exception e)
            {
                // Only costs the pre-ticked boxes; KeptOutBehavior falls back to marking the titles.
                Debug.Print("[KeepOutOfBattle] popup pre-selection patch failed: " + e);
            }
        }

        internal static void Warn(string message)
        {
            Debug.Print("[KeepOutOfBattle] " + message);
            InformationManager.DisplayMessage(new InformationMessage("Keep Out of Battle: " + message.Split('\n')[0], Colors.Yellow));
        }

        // For the code that runs inside the game's own methods: log the first failure of each site, never rethrow.
        internal static void LogOnce(string where, Exception e)
        {
            if (!Logged.Add(where))
                return;
            Debug.Print("[KeepOutOfBattle] " + where + " failed, using vanilla behaviour: " + e);
        }
    }
}
