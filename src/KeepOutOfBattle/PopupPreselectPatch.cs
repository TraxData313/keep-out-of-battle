using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade.ViewModelCollection.Inquiries;

namespace KeepOutOfBattle
{
    // The game's multi-selection popup always opens with nothing ticked. For our popup only, tick the heroes
    // who are already kept out, so it shows the current state and "Done" without changes keeps it.
    // Setting InquiryElementVM.IsSelected runs the popup's own selection callback, so its count stays right.
    internal static class PopupPreselectPatch
    {
        private static MultiSelectionInquiryData? _expected;
        private static HashSet<object>? _preselected;

        public static bool IsActive { get; private set; }

        public static void Apply(Harmony harmony)
        {
            var setData = AccessTools.Method(typeof(MultiSelectionQueryPopUpVM), nameof(MultiSelectionQueryPopUpVM.SetData))
                ?? throw new MissingMethodException("MultiSelectionQueryPopUpVM.SetData");
            harmony.Patch(setData, postfix: new HarmonyMethod(typeof(PopupPreselectPatch), nameof(SetDataPostfix)));
            IsActive = true;
        }

        public static void Expect(MultiSelectionInquiryData data, IEnumerable<object> preselected)
        {
            _expected = data;
            _preselected = new HashSet<object>(preselected);
        }

        private static void SetDataPostfix(MultiSelectionQueryPopUpVM __instance, MultiSelectionInquiryData data)
        {
            if (_expected == null || !ReferenceEquals(data, _expected))
                return;
            var preselected = _preselected;
            _expected = null;
            _preselected = null;
            try
            {
                if (preselected == null || preselected.Count == 0)
                    return;
                foreach (var vm in __instance.InquiryElements.Where(vm => vm.InquiryElement?.Identifier != null && preselected.Contains(vm.InquiryElement.Identifier)))
                    vm.IsSelected = true;
            }
            catch (Exception e)
            {
                SubModule.LogOnce("MultiSelectionQueryPopUpVM.SetData postfix", e);
            }
        }
    }
}
