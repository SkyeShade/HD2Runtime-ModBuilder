using System.Text.Json;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

// Attack output swaps (format 11). Each is validated against the SDK's active projectile source and output catalog before it is generated,
// and written as its own guarded patch: target = the host's active source, expect = that source's current projectile handle,
// value = hd2.attack_output(semantic ID). Runtime loads the donor's package automatically before the write (0.27 asset loader), so nothing
// extra is generated for assets. Hosts (0.28.0 unified projectile hosts, attack-outputs.md):
//   player component   target hd2.weapon(W):attack(role)                              expect <target>:projectile()   hd2.fields.attack.projectile
//   player ammunition  target hd2.weapon(W):ammunition()                              expect <target>:projectile()   hd2.fields.ammunition.projectile
//   support            target hd2.support_weapon(S):attack(role)                       expect <target>:projectile()   hd2.fields.attack.projectile
//   mounted            target <mount>:attack(role):projectile_source().target          expect <mount>:attack(role)    hd2.fields.attack.projectile
//     where <mount> is hd2.vehicle(V):weapon(M), or hd2.backpack(B):drone():weapon() for a Guard Dog drone gun.
public static class AttackOutputChangeService
{
    public static readonly string[] FlagOrder = ["allow_shared", "allow_unverified_effect", "allow_unverified_reference"];
    public static AttackOutputCatalog Catalog(SdkMetadata sdk) => sdk.AttackOutputs
        ?? throw new InvalidDataException(CoreText.Format("Messages.Build.Output.SdkMissing", AttackOutputReader.FileName));
    public static (AttackOutputHost Host, AttackOutputDonor Donor) Resolve(SdkMetadata sdk, string weapon, string role, string output) =>
        Resolve(sdk, AttackOutputChange.PlayerHost, weapon, role, output);
    public static (AttackOutputHost Host, AttackOutputDonor Donor) Resolve(SdkMetadata sdk, string kind, string weapon, string role, string output)
    {
        var catalog = Catalog(sdk);
        var host = (kind == AttackOutputChange.PlayerHost ? catalog.Host(sdk.PlayerWeapons!, weapon, role, out var reason) : ProjectileHosts.Host(sdk, kind, weapon, role, out reason))
            ?? throw new InvalidDataException(weapon + " · " + role.Replace('_', ' ') + ": " + reason);
        var donor = catalog.Donors(host).FirstOrDefault(d => d.Output.SemanticId == output)
            ?? throw new InvalidDataException(CoreText.Format("Messages.Build.Output.NoLongerSelectable", weapon, output));
        if (!donor.Allowed) throw new InvalidDataException(donor.Refusal);
        return (host, donor);
    }
    // Semantic content of the pair: host source and ammunition row, output identity and class, package, the published opt-ins. Player hosts
    // keep the exact evidence shape they were saved with; support and mounted hosts add their kind, shared entity and live-proven donors.
    public static string Evidence(AttackOutputHost host, AttackOutputDonor donor) => SupportChangeService.Hash(host.HostKind == AttackOutputChange.PlayerHost
        ? JsonSerializer.Serialize(new
        {
            host.Weapon, host.Role, host.Mechanism, host.CompatibilityClass, Ammunition = host.Ammunition?.SemanticId, AmmunitionItem = host.Ammunition?.Item,
            donor.Output.SemanticId, OutputClass = donor.Output.CompatibilityClass, donor.Output.Kind, donor.Output.Owner, donor.Output.Package, donor.CrossClass, donor.Acknowledgements,
        })
        : JsonSerializer.Serialize(new
        {
            host.HostKind, host.Weapon, host.Role, host.Mechanism, host.CompatibilityClass, host.BaseAcknowledgements, host.SharedEntity, host.CrossClassHost,
            donor.Output.SemanticId, OutputClass = donor.Output.CompatibilityClass, donor.Output.Kind, donor.Output.Owner, donor.Output.Package, donor.CrossClass,
            donor.ProvenOnHost, donor.Acknowledgements,
        }));
    public static AttackOutputChange Create(SdkMetadata sdk, string weapon, string role, string output) => Create(sdk, AttackOutputChange.PlayerHost, weapon, role, output);
    public static AttackOutputChange Create(SdkMetadata sdk, string kind, string weapon, string role, string output)
    {
        var (host, donor) = Resolve(sdk, kind, weapon, role, output);
        return new() { Weapon = weapon, AttackRole = role, Mechanism = host.Mechanism, Output = output, OutputName = donor.Output.Label,
            Acknowledgements = donor.Acknowledgements, Evidence = Evidence(host, donor), BaselineSdkVersion = sdk.Version,
            HostKind = kind == AttackOutputChange.PlayerHost ? null : kind };
    }
    public static void Validate(SdkMetadata sdk, AttackOutputChange c)
    {
        var (host, donor) = Resolve(sdk, c.Kind, c.Weapon, c.AttackRole, c.Output);
        if (c.Mechanism != host.Mechanism || c.Evidence != Evidence(host, donor) || !c.Acknowledgements.Order(StringComparer.Ordinal).SequenceEqual(donor.Acknowledgements))
            throw new InvalidDataException(CoreText.Format("Messages.Build.Output.Changed", c.Weapon, c.OutputName));
    }
    // One write per weapon entity: a mount that is the same entity as another (the Gunner FRV and the Super Earth FRV gun) is one host.
    public static string EntityKey(SdkMetadata sdk, AttackOutputChange c)
    {
        var shared = c.Kind == AttackOutputChange.VehicleHost ? sdk.AttackOutputs?.Source(c.Kind, c.Weapon, c.AttackRole)?.SharedEntity ?? [] : [];
        return c.Kind + "|" + shared.Append(c.Weapon).Order(StringComparer.Ordinal).First() + "|" + c.AttackRole;
    }
    // The Lua that names a host's mounted weapon: the vehicle mount, or the Guard Dog drone that carries it.
    public static string MountLua(SdkMetadata sdk, string weapon)
    {
        var (vehicle, mount) = VehicleWeaponReader.Split(weapon);
        return ProjectileHosts.Carrier(sdk, weapon) is { } backpack ? "hd2.backpack(" + LuaGenerator.Quote(backpack) + "):drone():weapon()"
            : "hd2.vehicle(" + LuaGenerator.Quote(vehicle) + "):weapon(" + LuaGenerator.Quote(mount) + ")";
    }
    public static (string Target, string Field, string Expect) HostLua(SdkMetadata sdk, AttackOutputChange c)
    {
        var role = LuaGenerator.Quote(c.AttackRole);
        switch (c.Kind)
        {
            case AttackOutputChange.SupportHost:
                var attack = "hd2.support_weapon(" + LuaGenerator.Quote(c.Weapon) + "):attack(" + role + ")";
                return (attack, "hd2.fields.attack.projectile", attack + ":projectile()");
            case AttackOutputChange.VehicleHost:
                var mounted = MountLua(sdk, c.Weapon) + ":attack(" + role + ")";
                return (mounted + ":projectile_source().target", "hd2.fields.attack.projectile", mounted);
            default:
                var weapon = "hd2.weapon(" + LuaGenerator.Quote(c.Weapon) + ")";
                var target = c.Mechanism == AttackOutputHost.Component ? weapon + ":attack(" + role + ")" : weapon + ":ammunition()";
                return (target, c.Mechanism == AttackOutputHost.Component ? "hd2.fields.attack.projectile" : "hd2.fields.ammunition.projectile", target + ":projectile()");
        }
    }
    public static IReadOnlyList<string> Operations(ModProject project, SdkMetadata sdk)
    {
        var active = (project.AttackOutputChanges ?? []).Where(c => c.Enabled).OrderBy(c => c.Kind == AttackOutputChange.PlayerHost ? 0 : 1)
            .ThenBy(c => c.Kind, StringComparer.Ordinal).ThenBy(c => c.Weapon, StringComparer.Ordinal).ThenBy(c => c.AttackRole, StringComparer.Ordinal).ToArray();
        if (active.Length == 0) return [];
        if (active.GroupBy(c => (c.Kind, c.Weapon, c.AttackRole)).Any(g => g.Count() > 1)) throw new InvalidDataException(CoreText.Get("Messages.Build.Output.OnePerAttack"));
        if (active.GroupBy(c => EntityKey(sdk, c)).FirstOrDefault(g => g.Count() > 1) is { } same)
            throw new InvalidDataException(CoreText.Format("Messages.Build.Output.SameEntity", string.Join(", ", same.Select(c => c.Weapon))));
        var output = new List<string>();
        foreach (var c in active)
        {
            Validate(sdk, c);
            if (c.IsPlayer && project.ProjectileChanges.Any(p => p.Enabled && p.Weapon == c.Weapon && p.AttackRole == c.AttackRole))
                throw new InvalidDataException(CoreText.Format("Messages.Build.Output.SwapConflict", c.Weapon));
            var (target, field, expect) = HostLua(sdk, c);
            // Player ids keep their original digest; other hosts add their kind.
            var key = c.IsPlayer ? project.ResourceId + "\n" + c.Weapon + "\n" + c.AttackRole : project.ResourceId + "\n" + c.Kind + "\n" + c.Weapon + "\n" + c.AttackRole;
            var body = "{\n    id=" + LuaGenerator.Quote("output-" + SupportChangeService.Hash(key)[..24]) + ",\n    target=" + target + ",\n";
            foreach (var flag in FlagOrder.Where(c.Acknowledgements.Contains)) body += "    " + flag + "=true,\n";
            body += "    field=" + field + ",\n    expect=" + expect + ",\n    value=hd2.attack_output(" + LuaGenerator.Quote(c.Output) + "),\n}";
            output.Add(OptionBindings.Wrap(null, "patch", body, c.EnsureEnabled, []));
        }
        return output;
    }
}
