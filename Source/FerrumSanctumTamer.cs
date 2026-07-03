using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Code.UI.MVVM.VM.Other;
using Kingmaker.Code.UI.MVVM.VM.Overtips.Unit;
using Kingmaker.Code.UI.MVVM.VM.Party;
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Templates;
using Kingmaker.Designers.Mechanics.Facts.Damage;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Mechanics.Entities;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using UnityEngine;
using UnityModManagerNet;

namespace FerrumSanctumTamer
{
public static class Main
{
    internal const string ModId = "FerrumSanctumTamer";
    internal const string FerrumSanctumGuid = "780497b04a0944e59cb57e68bb9775c4";

    internal static Settings Settings;
    internal static UnityModManager.ModEntry ModEntry;
    internal static bool Enabled;
    internal static readonly HashSet<UnitBuffPartVM> OvertipBuffParts = new HashSet<UnitBuffPartVM>();

    private static Harmony s_Harmony;
    private static float s_NextStripTime;
    private static bool s_RestoringFerrum;
    private static readonly List<FerrumRestoreRecord> FerrumRestoreRecords = new List<FerrumRestoreRecord>();
    private static readonly FieldInfo CollapsedBuffsField = AccessTools.Field(typeof(UnitBuffPartVM), "m_CollapsedBuffs");
    private static readonly FieldInfo TooltipBuffDescriptionField = AccessTools.Field(typeof(TooltipTemplateBuff), "m_Desc");

    public static bool Load(UnityModManager.ModEntry modEntry)
    {
        ModEntry = modEntry;
        Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);

        s_Harmony = new Harmony(ModId);
        s_Harmony.PatchAll(Assembly.GetExecutingAssembly());

        modEntry.OnToggle = OnToggle;
        modEntry.OnGUI = OnGUI;
        modEntry.OnSaveGUI = OnSaveGUI;
        modEntry.OnUpdate = OnUpdate;
        Enabled = modEntry.Enabled;

        modEntry.Logger.Log("Loaded.");
        return true;
    }

    private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
    {
        Enabled = value;
        if (!Enabled)
        {
            RestoreCachedFerrumApplications();
        }
        RefreshOvertips();
        return true;
    }

    private static void OnGUI(UnityModManager.ModEntry modEntry)
    {
        bool oldHide = Settings.HideOverheadIcon;
        FerrumMode oldMode = Settings.Mode;

        GUILayout.Label("Overhead display");
        Settings.HideOverheadIcon = GUILayout.Toggle(
            Settings.HideOverheadIcon,
            "Hide Ferrum Sanctum above characters' heads");

        GUILayout.Space(8f);
        GUILayout.Label("Ferrum Sanctum mechanical value");
        DrawModeButton("Vanilla: 15% per stack", FerrumMode.Vanilla15);
        DrawModeButton("Reduced: 10% per stack", FerrumMode.Reduce10);
        DrawModeButton("Reduced: 5% per stack", FerrumMode.Reduce5);
        DrawModeButton("Disabled: 0%, and block new Ferrum Sanctum applications", FerrumMode.Disable0);

        GUILayout.Space(8f);
        GUILayout.Label("Changing this setting should immediately affect gameplay.");

        if (oldHide != Settings.HideOverheadIcon || oldMode != Settings.Mode)
        {
            if (oldMode == FerrumMode.Disable0 && Settings.Mode != FerrumMode.Disable0)
            {
                RestoreCachedFerrumApplications();
            }

            RefreshOvertips();
            if (Settings.Mode == FerrumMode.Disable0 && IsPatchActive)
            {
                StripFerrumFromKnownUnits();
            }
        }
    }

    private static void OnUpdate(UnityModManager.ModEntry modEntry, float delta)
    {
        if (!IsPatchActive || Settings.Mode != FerrumMode.Disable0 || Time.realtimeSinceStartup < s_NextStripTime)
        {
            return;
        }

        s_NextStripTime = Time.realtimeSinceStartup + 1f;
        StripFerrumFromKnownUnits();
    }

    private static void DrawModeButton(string label, FerrumMode mode)
    {
        bool selected = Settings.Mode == mode;
        bool next = GUILayout.Toggle(selected, label);
        if (next && !selected)
        {
            Settings.Mode = mode;
        }
    }

    private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
    {
        Settings.Save(modEntry);
    }

    internal static bool IsFerrum(BlueprintBuff blueprint)
    {
        return blueprint != null && string.Equals(blueprint.AssetGuid, FerrumSanctumGuid, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsFerrum(Buff buff)
    {
        return buff != null && IsFerrum(buff.Blueprint);
    }

    internal static bool IsPatchActive
    {
        get { return Enabled; }
    }

    internal static bool ShouldShowOverhead(Buff buff)
    {
        return !IsPatchActive || !Settings.HideOverheadIcon || !IsFerrum(buff);
    }

    internal static void RemoveFerrumFromOvertip(UnitBuffPartVM buffPart)
    {
        if (!IsPatchActive || !Settings.HideOverheadIcon || buffPart == null || !OvertipBuffParts.Contains(buffPart))
        {
            return;
        }

        RemoveFerrumFromCollapsedCache(buffPart);
        for (int i = buffPart.Buffs.Count - 1; i >= 0; i--)
        {
            BuffVM vm = buffPart.Buffs[i];
            if (vm != null && IsFerrum(vm.Buff))
            {
                vm.Dispose();
                buffPart.Buffs.RemoveAt(i);
            }
        }
    }

    internal static void CacheFerrumApplication(MechanicEntity unit, BlueprintBuff blueprint, MechanicEntity caster, MechanicsContext context, BuffDuration duration, int rank)
    {
        if (unit == null || blueprint == null || !IsFerrum(blueprint) || s_RestoringFerrum)
        {
            return;
        }

        FerrumRestoreRecords.Add(new FerrumRestoreRecord(unit, blueprint, caster, context, duration, Math.Max(1, rank)));
    }

    private static void RefreshOvertips()
    {
        foreach (UnitBuffPartVM buffPart in OvertipBuffParts.ToList())
        {
            try
            {
                RemoveFerrumFromCollapsedCache(buffPart);
                buffPart?.UpdateData();
            }
            catch (Exception ex)
            {
                ModEntry?.Logger?.LogException("RefreshOvertips", ex);
            }
        }
    }

    private static void RemoveFerrumFromCollapsedCache(UnitBuffPartVM buffPart)
    {
        try
        {
            Dictionary<BlueprintBuff, BuffVM> collapsed = CollapsedBuffsField?.GetValue(buffPart) as Dictionary<BlueprintBuff, BuffVM>;
            if (collapsed == null)
            {
                return;
            }

            foreach (BlueprintBuff blueprint in collapsed.Keys.ToList())
            {
                if (IsFerrum(blueprint))
                {
                    collapsed.Remove(blueprint);
                }
            }
        }
        catch (Exception ex)
        {
            ModEntry?.Logger?.LogException("RemoveFerrumFromCollapsedCache", ex);
        }
    }

    private static void RestoreCachedFerrumApplications()
    {
        if (FerrumRestoreRecords.Count == 0)
        {
            return;
        }

        int restored = 0;
        s_RestoringFerrum = true;
        try
        {
            foreach (FerrumRestoreRecord record in FerrumRestoreRecords.ToList())
            {
                if (record.Unit == null || record.Unit.IsDisposed || record.Unit.Buffs == null)
                {
                    continue;
                }

                Buff restoredBuff = TryRestoreFerrum(record);
                if (restoredBuff == null)
                {
                    continue;
                }

                for (int i = 1; i < record.Rank; i++)
                {
                    restoredBuff.AddRank();
                }

                restored++;
            }
        }
        catch (Exception ex)
        {
            ModEntry?.Logger?.LogException("RestoreCachedFerrumApplications", ex);
        }
        finally
        {
            s_RestoringFerrum = false;
            FerrumRestoreRecords.Clear();
        }

        if (restored > 0)
        {
            RefreshOvertips();
            ModEntry?.Logger?.Log("Restored Ferrum Sanctum buffs: " + restored);
        }
    }

    private static Buff TryRestoreFerrum(FerrumRestoreRecord record)
    {
        try
        {
            return record.Unit.Buffs.Add(record.Blueprint, record.Caster, record.Context, record.Duration);
        }
        catch (Exception ex)
        {
            ModEntry?.Logger?.LogException("RestoreFerrumWithContext", ex);
        }

        try
        {
            return record.Unit.Buffs.Add(record.Blueprint);
        }
        catch (Exception ex)
        {
            ModEntry?.Logger?.LogException("RestoreFerrumFallback", ex);
            return null;
        }
    }

    private static void StripFerrumFromKnownUnits()
    {
        HashSet<AbstractUnitEntity> units = new HashSet<AbstractUnitEntity>();
        try
        {
            if (Game.Instance?.State?.AllUnits != null)
            {
                foreach (AbstractUnitEntity unit in Game.Instance.State.AllUnits)
                {
                    if (unit != null)
                    {
                        units.Add(unit);
                    }
                }
            }

            if (Game.Instance?.Player?.AllCrossSceneUnits != null)
            {
                foreach (BaseUnitEntity unit in Game.Instance.Player.AllCrossSceneUnits)
                {
                    if (unit != null)
                    {
                        units.Add(unit);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ModEntry?.Logger?.LogException("CollectUnits", ex);
        }

        int removed = 0;
        foreach (AbstractUnitEntity unit in units)
        {
            removed += StripFerrumFromUnit(unit);
        }

        if (removed > 0)
        {
            RefreshOvertips();
            ModEntry?.Logger?.Log("Stripped Ferrum Sanctum buffs: " + removed);
        }
    }

    private static int StripFerrumFromUnit(AbstractUnitEntity unit)
    {
        if (unit?.Buffs == null || unit.Facts == null)
        {
            return 0;
        }

        int removed = 0;
        foreach (Buff buff in unit.Buffs.Enumerable.ToList())
        {
            if (IsFerrum(buff))
            {
                CacheFerrumApplication(unit, buff.Blueprint, buff.Context?.MaybeCaster, buff.Context, new BuffDuration(buff), buff.Rank);
                unit.Facts.Remove(buff);
                removed++;
            }
        }

        return removed;
    }

    internal static int MechanicalPercentPerStack
    {
        get
        {
            if (!Enabled)
            {
                return 15;
            }

            if (!IsPatchActive)
            {
                return 15;
            }

            switch (Settings.Mode)
            {
                case FerrumMode.Reduce10:
                    return 10;
                case FerrumMode.Reduce5:
                    return 5;
                case FerrumMode.Disable0:
                    return 0;
                default:
                    return 15;
            }
        }
    }

    internal static string UpdateFerrumDescriptionPercent(string description)
    {
        if (string.IsNullOrEmpty(description))
        {
            return description;
        }

        string percent = MechanicalPercentPerStack.ToString();
        return description
            .Replace("+15% more", "+" + percent + "% more")
            .Replace("+15 % more", "+" + percent + "% more")
            .Replace("+15\u00a0% more", "+" + percent + "% more");
    }

    internal static void UpdateFerrumTooltipDescription(TooltipTemplateBuff tooltip)
    {
        if (tooltip == null)
        {
            return;
        }

        BlueprintBuff blueprint = tooltip.Buff?.Blueprint ?? tooltip.BlueprintBuff;
        if (!IsFerrum(blueprint))
        {
            return;
        }

        try
        {
            string description = TooltipBuffDescriptionField?.GetValue(tooltip) as string;
            TooltipBuffDescriptionField?.SetValue(tooltip, UpdateFerrumDescriptionPercent(description));
        }
        catch (Exception ex)
        {
            ModEntry?.Logger?.LogException("UpdateFerrumTooltipDescription", ex);
        }
    }
}

internal sealed class FerrumRestoreRecord
{
    public readonly MechanicEntity Unit;
    public readonly BlueprintBuff Blueprint;
    public readonly MechanicEntity Caster;
    public readonly MechanicsContext Context;
    public readonly BuffDuration Duration;
    public readonly int Rank;

    public FerrumRestoreRecord(MechanicEntity unit, BlueprintBuff blueprint, MechanicEntity caster, MechanicsContext context, BuffDuration duration, int rank)
    {
        Unit = unit;
        Blueprint = blueprint;
        Caster = caster;
        Context = context;
        Duration = duration;
        Rank = rank;
    }
}

public enum FerrumMode
{
    Vanilla15,
    Reduce10,
    Reduce5,
    Disable0
}

public class Settings : UnityModManager.ModSettings
{
    public bool HideOverheadIcon = true;
    public FerrumMode Mode = FerrumMode.Vanilla15;
}

[HarmonyPatch(typeof(OvertipEntityUnitVM), MethodType.Constructor, new[] { typeof(AbstractUnitEntity) })]
internal static class OvertipEntityUnitVM_Ctor_Patch
{
    private static void Postfix(OvertipEntityUnitVM __instance)
    {
        if (__instance?.BuffPartVM == null)
        {
            return;
        }

        Main.OvertipBuffParts.Add(__instance.BuffPartVM);
        Main.RemoveFerrumFromOvertip(__instance.BuffPartVM);
    }
}

[HarmonyPatch(typeof(UnitBuffPartVM), "HandleBuffDidAdded")]
internal static class UnitBuffPartVM_HandleBuffDidAdded_Patch
{
    private static bool Prefix(UnitBuffPartVM __instance, Buff buff)
    {
        if (!Main.OvertipBuffParts.Contains(__instance))
        {
            return true;
        }

        return Main.ShouldShowOverhead(buff);
    }
}

[HarmonyPatch(typeof(UnitBuffPartVM), "UpdateData")]
internal static class UnitBuffPartVM_UpdateData_Patch
{
    private static void Postfix(UnitBuffPartVM __instance)
    {
        Main.RemoveFerrumFromOvertip(__instance);
    }
}

[HarmonyPatch(typeof(TooltipTemplateBuff), "Prepare")]
internal static class TooltipTemplateBuff_Prepare_Patch
{
    private static void Postfix(TooltipTemplateBuff __instance)
    {
        Main.UpdateFerrumTooltipDescription(__instance);
    }
}

[HarmonyPatch(typeof(BuffCollection), "Add", new[] { typeof(BlueprintBuff), typeof(MechanicEntity), typeof(MechanicsContext), typeof(BuffDuration) })]
internal static class BuffCollection_Add_Patch
{
    private static bool Prefix(BuffCollection __instance, BlueprintBuff blueprint, MechanicEntity caster, MechanicsContext parentContext, BuffDuration duration, ref Buff __result)
    {
        if (Main.IsPatchActive && Main.Settings.Mode == FerrumMode.Disable0 && Main.IsFerrum(blueprint))
        {
            Main.CacheFerrumApplication(__instance?.Owner, blueprint, caster, parentContext, duration, 1);
            __result = null;
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(WarhammerDamageModifier), "TryApply")]
internal static class WarhammerDamageModifier_TryApply_Patch
{
    private static readonly Dictionary<WarhammerDamageModifier, int> OriginalValues = new Dictionary<WarhammerDamageModifier, int>();

    private static bool Prefix(WarhammerDamageModifier __instance)
    {
        if (!Main.IsPatchActive || !IsFerrumModifier(__instance))
        {
            return true;
        }

        int percent = Main.MechanicalPercentPerStack;
        if (percent <= 0)
        {
            return false;
        }

        ContextValueModifier modifier = __instance.PercentDamageModifier;
        if (modifier == null || !modifier.Enabled)
        {
            return true;
        }

        OriginalValues[__instance] = modifier.Value;
        modifier.Value = percent;
        return true;
    }

    private static void Postfix(WarhammerDamageModifier __instance)
    {
        if (OriginalValues.TryGetValue(__instance, out int original))
        {
            __instance.PercentDamageModifier.Value = original;
            OriginalValues.Remove(__instance);
        }
    }

    private static bool IsFerrumModifier(WarhammerDamageModifier modifier)
    {
        return modifier != null
            && modifier.Fact != null
            && modifier.Fact.Blueprint is BlueprintBuff blueprint
            && Main.IsFerrum(blueprint);
    }
}
}
