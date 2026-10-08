using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Services;

// HD2Runtime 0.30.0 armor. Kit piece weights, class values and the damage curve are entity fields (SetEntityAsync); the local player's armor
// values and passive override are ModProject.PlayerArmor (format 14), saved here with the same autosave / rollback pattern.
public sealed partial class BuilderWorkspace
{
    public ArmorCatalog? ArmorCatalog => (Metadata ?? SdkStatus?.Installed)?.Entities?.Armor;
    // Every armor piece of a kit at one weight, in one saved edit (pieces already at that weight in vanilla drop their edit).
    public Task SetKitWeightAsync(string kit, string weight) =>
        SetEntitiesAsync([.. (ArmorCatalog?.KitFields(kit) ?? []).Select(f => (f.InstanceKey, weight))]);
    // The class tables and the curve as this project leaves them (enabled edits over the published values): what the kit preview uses.
    public IReadOnlyDictionary<string, ArmorMath.ClassValues> ArmorClassValues()
    {
        var c = ArmorCatalog!; var values = new Dictionary<string, ArmorMath.ClassValues>(ArmorMath.Vanilla(c), StringComparer.Ordinal);
        foreach (var x in c.Classes)
            values[x.Name] = new(ArmorValue(c.ClassFields(x.Name), "armor_class.rating"), ArmorValue(c.ClassFields(x.Name), "armor_class.speed"), ArmorValue(c.ClassFields(x.Name), "armor_class.stamina"));
        return values;
    }
    public IReadOnlyList<ArmorCurvePoint> ArmorCurve()
    {
        var c = ArmorCatalog!; var fields = c.CurveFields.ToArray();
        return [.. c.DamageCurve.Points.Select((p, i) => p with { Damage = ArmorValue(fields, "armor_damage_curve.at_" + Suffix(p.ArmorValue)) })];
        static string Suffix(double a) => a < 0 ? "minus_" + (-a).ToString(System.Globalization.CultureInfo.InvariantCulture) : a.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    // A kit's weights as this project leaves them.
    public IReadOnlyDictionary<string, string> ArmorKitWeights(ArmorKit kit)
    {
        var weights = new Dictionary<string, string>(kit.Weights, StringComparer.Ordinal);
        foreach (var f in ArmorCatalog!.KitFields(kit.Id))
            if (EntityChangeService.Saved(Project, f) is { Enabled: true } c && c.DesiredValue.ValueKind == System.Text.Json.JsonValueKind.String) weights[f.UiGroup!] = c.DesiredValue.GetString()!;
        return weights;
    }
    private double ArmorValue(IEnumerable<EntityField> fields, string semanticFieldId)
    {
        var f = fields.First(x => x.SemanticFieldId == semanticFieldId);
        return EntityChangeService.Saved(Project, f) is { Enabled: true } c && c.DesiredValue.ValueKind == System.Text.Json.JsonValueKind.Number ? c.DesiredValue.GetDouble() : f.CurrentDefault.GetDouble();
    }

    public Task SetPlayerArmorBonusAsync(double? value) => EditPlayerArmorAsync(a => a.ArmorBonus = value);
    public Task SetPlayerStaminaFactorAsync(double? value) => EditPlayerArmorAsync(a => a.StaminaFactor = value);
    // The passive override: an armor passive (null keeps the kit's own) and a second passive (null: none).
    public Task SetPlayerPassivesAsync(int? armor, int? second) => EditPlayerArmorAsync(a => { a.PassiveArmor = armor; a.PassiveSecond = second; });
    public Task ResetPlayerArmorAsync(bool stats = true, bool passives = true) => EditPlayerArmorAsync(a =>
    {
        if (stats) { a.ArmorBonus = null; a.StaminaFactor = null; }
        if (passives) { a.PassiveArmor = null; a.PassiveSecond = null; }
    });
    private async Task EditPlayerArmorAsync(Action<PlayerArmorSettings> edit)
    {
        var project = Project; await weaponEditGate.WaitAsync();
        try
        {
            if (project == null || !ReferenceEquals(project, Project)) throw new InvalidOperationException(CoreText.Get("Messages.Workspace.ProjectChanged"));
            var previous = project.PlayerArmor; var format = project.FormatVersion;
            var next = new PlayerArmorSettings { ArmorBonus = previous?.ArmorBonus, StaminaFactor = previous?.StaminaFactor, PassiveArmor = previous?.PassiveArmor, PassiveSecond = previous?.PassiveSecond };
            try
            {
                edit(next);
                // What Runtime would refuse is refused here, where it is edited (nothing is saved).
                ArmorPlayerLua.Validate(next, Metadata!);
                project.PlayerArmor = next.IsEmpty ? null : next;
                await SaveChangesAsync();
            }
            catch { project.PlayerArmor = previous; project.FormatVersion = format; throw; }
        }
        finally { weaponEditGate.Release(); }
    }
}
