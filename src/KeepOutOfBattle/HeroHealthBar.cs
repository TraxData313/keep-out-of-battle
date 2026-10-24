using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;

namespace KeepOutOfBattle
{
    // A health bar with the percentage over the bottom of each hero's portrait in our picker (the game's
    // multi-selection popup), so a badly hurt hero isn't sent in by mistake. It is the party screen's own hero
    // health bar widget, which also picks the colour: red while wounded, gold, green from 90%.
    [PrefabExtension("MultiSelectionQueryPopup", "descendant::ImageIdentifierWidget[@DataSource='{ImageIdentifier}']")]
    public sealed class HeroHealthBarPrefab : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        [PrefabExtensionText]
        public string Content => @"
<Widget DoNotAcceptEvents=""true"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""Fixed"" SuggestedHeight=""15"" VerticalAlignment=""Bottom"" MarginLeft=""4"" MarginRight=""4"" MarginBottom=""4"" Sprite=""BlankWhiteSquare_9"" Color=""#0f0f0eE6"" IsVisible=""@IsKeepOutHealthVisible"">
  <Children>
    <PartyHealthFillBarWidget DoNotAcceptEvents=""true"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" MarginLeft=""1"" MarginRight=""1"" MarginTop=""1"" MarginBottom=""1"" Brush=""KeepOutOfBattle.HealthBarFill"" DoNotUseCustomScale=""true"" Health=""@KeepOutHealth"" HealthText=""HealthText"" IsWounded=""@IsKeepOutWounded"" MaxAmount=""100"">
      <Children>
        <TextWidget Id=""HealthText"" DoNotAcceptEvents=""true"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Brush=""Party.TroopTupple.HealthBarText"" />
      </Children>
    </PartyHealthFillBarWidget>
  </Children>
</Widget>";
    }

    // Every entry of every multi-selection popup gets this; only the heroes of our picker show the bar.
    [ViewModelMixin]
    public sealed class HeroHealthBarMixin : BaseViewModelMixin<InquiryElementVM>
    {
        private static readonly ConditionalWeakTable<InquiryElement, object> Ours = new ConditionalWeakTable<InquiryElement, object>();

        private readonly Hero? _hero;

        public HeroHealthBarMixin(InquiryElementVM vm) : base(vm)
        {
            var element = vm.InquiryElement;
            if (element != null && Ours.TryGetValue(element, out _))
                _hero = element.Identifier as Hero;
        }

        // Called by KeptOutBehavior for the entries of the picker it is about to open.
        internal static void Mark(IEnumerable<InquiryElement> elements)
        {
            foreach (var element in elements)
                Ours.GetValue(element, _ => new object());
        }

        // As the party screen rounds it.
        internal static int HealthPercent(Hero hero) =>
            hero.MaxHitPoints <= 0 ? 0 : MathF.Ceiling(hero.HitPoints * 100f / hero.MaxHitPoints);

        [DataSourceProperty] public bool IsKeepOutHealthVisible => _hero != null;

        [DataSourceProperty] public int KeepOutHealth => _hero == null ? 0 : HealthPercent(_hero);

        [DataSourceProperty] public bool IsKeepOutWounded => _hero?.IsWounded == true;
    }
}
