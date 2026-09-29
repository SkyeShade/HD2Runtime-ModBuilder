using System.Text.Json;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

// Attack output swaps (format 11). Each is validated against the SDK's active projectile source and output catalog before it is generated,
// and written as its own guarded patch: target = the host's active source (weapon:attack(role) or weapon:ammunition()), expect = that
// source's current projectile handle, value = hd2.attack_output(semantic ID). Runtime loads the donor's package automatically before the
// write (0.27 asset loader), so nothing extra is generated for assets.
public static class AttackOutputChangeService
{
    public static readonly string[] FlagOrder = ["allow_shared", "allow_unverified_effect", "allow_unverified_reference"];
    public static AttackOutputCatalog Catalog(SdkMetadata sdk) => sdk.AttackOutputs
        ?? throw new InvalidDataException("Attack outputs need an SDK that publishes " + AttackOutputReader.FileName + " (a local HD2Runtime development SDK until 0.28.0).");
    public static (AttackOutputHost Host, AttackOutputDonor Donor) Resolve(SdkMetadata sdk, string weapon, string role, string output)
    {
        var catalog = Catalog(sdk);
        var host = catalog.Host(sdk.PlayerWeapons!, weapon, role, out var reason) ?? throw new InvalidDataException(weapon + " · " + role.Replace('_', ' ') + ": " + reason);
        var donor = catalog.Donors(host).FirstOrDefault(d => d.Output.SemanticId == output)
            ?? throw new InvalidDataException("The attack output is no longer selectable for " + weapon + ": " + output + ". Review or reset this change.");
        if (!donor.Allowed) throw new InvalidDataException(donor.Refusal);
        return (host, donor);
    }
    // Semantic content of the pair: host source and ammunition row, output identity and class, package, the published opt-ins.
    public static string Evidence(AttackOutputHost host, AttackOutputDonor donor) => SupportChangeService.Hash(JsonSerializer.Serialize(new
    {
        host.Weapon, host.Role, host.Mechanism, host.CompatibilityClass, Ammunition = host.Ammunition?.SemanticId, AmmunitionItem = host.Ammunition?.Item,
        donor.Output.SemanticId, OutputClass = donor.Output.CompatibilityClass, donor.Output.Kind, donor.Output.Owner, donor.Output.Package, donor.CrossClass, donor.Acknowledgements,
    }));
    public static AttackOutputChange Create(SdkMetadata sdk, string weapon, string role, string output)
    {
        var (host, donor) = Resolve(sdk, weapon, role, output);
        return new() { Weapon = weapon, AttackRole = role, Mechanism = host.Mechanism, Output = output, OutputName = donor.Output.Label,
            Acknowledgements = donor.Acknowledgements, Evidence = Evidence(host, donor), BaselineSdkVersion = sdk.Version };
    }
    public static void Validate(SdkMetadata sdk, AttackOutputChange c)
    {
        var (host, donor) = Resolve(sdk, c.Weapon, c.AttackRole, c.Output);
        if (c.Mechanism != host.Mechanism || c.Evidence != Evidence(host, donor) || !c.Acknowledgements.Order(StringComparer.Ordinal).SequenceEqual(donor.Acknowledgements))
            throw new InvalidDataException(c.Weapon + " → " + c.OutputName + ": the active projectile source, output or opt-ins changed. Choose the output again to review it, or reset the change.");
    }
    public static IReadOnlyList<string> Operations(ModProject project, SdkMetadata sdk)
    {
        var active = (project.AttackOutputChanges ?? []).Where(c => c.Enabled).OrderBy(c => c.Weapon, StringComparer.Ordinal).ThenBy(c => c.AttackRole, StringComparer.Ordinal).ToArray();
        if (active.Length == 0) return [];
        if (active.GroupBy(c => (c.Weapon, c.AttackRole)).Any(g => g.Count() > 1)) throw new InvalidDataException("An attack can fire only one output.");
        var output = new List<string>();
        foreach (var c in active)
        {
            Validate(sdk, c);
            if (project.ProjectileChanges.Any(p => p.Enabled && p.Weapon == c.Weapon && p.AttackRole == c.AttackRole))
                throw new InvalidDataException(c.Weapon + " has both a projectile swap and an attack output. Keep one of them.");
            var weapon = "hd2.weapon(" + LuaGenerator.Quote(c.Weapon) + ")";
            var target = c.Mechanism == AttackOutputHost.Component ? weapon + ":attack(" + LuaGenerator.Quote(c.AttackRole) + ")" : weapon + ":ammunition()";
            var field = c.Mechanism == AttackOutputHost.Component ? "hd2.fields.attack.projectile" : "hd2.fields.ammunition.projectile";
            var body = "{\n    id=" + LuaGenerator.Quote("output-" + SupportChangeService.Hash(project.ResourceId + "\n" + c.Weapon + "\n" + c.AttackRole)[..24]) + ",\n    target=" + target + ",\n";
            foreach (var flag in FlagOrder.Where(c.Acknowledgements.Contains)) body += "    " + flag + "=true,\n";
            body += "    field=" + field + ",\n    expect=" + target + ":projectile(),\n    value=hd2.attack_output(" + LuaGenerator.Quote(c.Output) + "),\n}";
            output.Add(OptionBindings.Wrap(null, "patch", body, c.EnsureEnabled, []));
        }
        return output;
    }
}
