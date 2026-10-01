using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Projects;

public static class ProjectIdentity
{
    public const string RuntimeModule = "mods/skyeshade/hd2runtime";
    // Suggested "mods/<author>/<mod>" from free-text names; null until both parts yield a valid identifier.
    // Taken IDs get a numeric suffix so the suggestion never collides with a library project.
    public static string? SuggestResourceId(string? author, string? modName, IEnumerable<string>? taken = null)
    {
        var (a, m) = (Slug(author, 48), Slug(modName, 64));
        if (a.Length == 0 || m.Length == 0) return null;
        var used = new HashSet<string>(taken ?? [], StringComparer.OrdinalIgnoreCase);
        for (var n = 1; n < 1000; n++)
        {
            var candidate = $"mods/{a}/{m}{(n == 1 ? "" : "_" + n)}";
            if (used.Contains(candidate)) continue;
            try { ValidateResource(candidate); return candidate; } catch (InvalidDataException) { return null; }
        }
        return null;
    }
    private static string Slug(string? text, int max)
    {
        var builder = new StringBuilder();
        foreach (var c in (text ?? "").Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            if (char.IsAsciiLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
            else if (builder.Length > 0 && builder[^1] != '_') builder.Append('_');
        }
        var slug = builder.ToString().Trim('_');
        return slug.Length <= max ? slug : slug[..max].TrimEnd('_');
    }
    public static void ValidateResource(string resource)
    {
        if (string.IsNullOrEmpty(resource) || !Regex.IsMatch(resource, @"\Amods/[A-Za-z0-9_]+(?:/[A-Za-z0-9_]+)+\z") ||
            resource.Equals(RuntimeModule, StringComparison.OrdinalIgnoreCase) || resource.StartsWith(RuntimeModule + "/", StringComparison.OrdinalIgnoreCase) ||
            resource.Equals("mods/codex/loader", StringComparison.OrdinalIgnoreCase) || Encoding.UTF8.GetByteCount($"-- HD2-Addon: {resource}\n") > 256)
            throw new InvalidDataException(CoreText.Get("Messages.Project.ResourceIdRules"));
    }
    public static Guid ManagerGuid(string resource)
    {
        ValidateResource(resource);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("hd2runtime-mod:" + resource))[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50); bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
    // Semantic graph identity only: target kind must agree with the graph path, and entity/weapon/attack are short published identifiers.
    private static bool ValidStratagemTarget(StratagemChange c)
    {
        static bool Id(string? s) => s != null && Regex.IsMatch(s, @"\A[a-z][a-z0-9_]{0,63}\z");
        return (c.TargetKind, c.Path) switch
        {
            _ when c.Zone != null && (c.TargetKind, c.Path) != ("deployed_entity", "damage_zone") => false,
            ("stratagem", "stratagem" or "eagle_rearm") => c.Entity == null && c.Weapon == null && c.Attack == null,
            ("stratagem", "attack") => c.Entity == null && c.Weapon == null && Id(c.Attack),
            ("deployed_entity", "deployed_entity" or "shield" or "turret" or "targeting" or "minefield") => Id(c.Entity) && c.Weapon == null && c.Attack == null,
            ("deployed_entity", "damage_zone") => Id(c.Entity) && Id(c.Zone) && c.Weapon == null && c.Attack == null,
            ("mounted_weapon", "weapon") => Id(c.Entity) && Id(c.Weapon) && c.Attack == null,
            ("mounted_weapon", "attack") => Id(c.Entity) && Id(c.Weapon) && Id(c.Attack),
            _ => false,
        };
    }
    // A row write's semantic value: a slot handle ("<output>#<slot>") or "none"; a native label / icon name ("stun", "ammo_stun", "auto").
    private static bool RowValue(string field, string v) => Generation.OutputRowChangeService.IsSlot(field)
        ? v == Generation.OutputRowChangeService.None || Regex.IsMatch(v, @"\Aoutput/v1/projectile/[a-z0-9-]{1,128}#(directDamage|impactExplosion|expiryExplosion)\z")
        : Regex.IsMatch(v, @"\A[a-z0-9_]{1,64}\z");
    private static bool Scalar(System.Text.Json.JsonElement v) => v.ValueKind is not (System.Text.Json.JsonValueKind.Undefined or System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.Array);
    private static bool NumberOrBool(System.Text.Json.JsonElement v) => v.ValueKind is System.Text.Json.JsonValueKind.Number or System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False;
    // 0.26.0 fire_mode.modes: 1-4 distinct named modes.
    private static bool ModeList(System.Text.Json.JsonElement v) => v.ValueKind == System.Text.Json.JsonValueKind.Array && v.GetArrayLength() is >= 1 and <= 4
        && v.EnumerateArray().All(m => m.ValueKind == System.Text.Json.JsonValueKind.String && Metadata.FireModes.Native.ContainsKey(m.GetString()!))
        && v.EnumerateArray().Select(m => m.GetString()).Distinct().Count() == v.GetArrayLength();
    // 1.4.0 composition values as saved: three rate slots, up to five trait IDs, a published name, or a function projectile token (its
    // expect may be the player catalog's {projectileType} baseline). Their meaning is checked against the SDK when the project is built.
    private static bool Name(System.Text.Json.JsonElement v) => v.ValueKind == System.Text.Json.JsonValueKind.String && Regex.IsMatch(v.GetString()!, @"\A[a-z][a-z0-9_]{0,63}\z");
    private static bool CompositionValue(string type, System.Text.Json.JsonElement v, bool expect) => type switch
    {
        Metadata.WeaponCapability.FireRateSet => v.ValueKind == System.Text.Json.JsonValueKind.Array && v.GetArrayLength() == 3
            && v.EnumerateArray().All(x => x.ValueKind == System.Text.Json.JsonValueKind.Number && double.IsFinite(x.GetDouble())),
        Metadata.WeaponCapability.TraitSet => v.ValueKind == System.Text.Json.JsonValueKind.Array && v.GetArrayLength() <= 5 && v.EnumerateArray().All(Name),
        Metadata.WeaponCapability.FunctionProjectileReference => expect && v.ValueKind == System.Text.Json.JsonValueKind.Object && v.TryGetProperty("projectileType", out var t)
                && t.ValueKind == System.Text.Json.JsonValueKind.Number
            || v.ValueKind == System.Text.Json.JsonValueKind.String && Regex.IsMatch(v.GetString()!, @"\A(?:none|native|output/v1/projectile/[a-z0-9-]{1,128})\z"),
        _ => Name(v),
    };
    private static bool Uses(System.Text.Json.JsonElement v) => v.ValueKind == System.Text.Json.JsonValueKind.Number
        || v.ValueKind == System.Text.Json.JsonValueKind.String && v.GetString() == Metadata.StratagemUses.Unlimited;
    // Format 8: SDK 0.26.0 edits (fire-mode lists, mission uses, effect acknowledgements, vehicle weapons, drop-pod payloads).
    // Format 9: SDK 0.27.0 throwable edits (older ModBuilder versions cannot read them).
    // Format 10: enemy and enemy-structure edits (0.28.0 development SDKs); older ModBuilder versions reject the format instead of the edits.
    // Format 11: hand-written Runtime Lua (custom src/addon.lua).
    // Format 11 (1.4.0) also covers 0.28.0 projectile hosts (support and mounted swaps), projectile-builder row writes, backpack-linked
    // entities and Guard Dog drone weapons (EntityChange.Linked), mounted-weapon status slots, weapon composition edits (rate slots, weapon
    // functions, function projectiles, armory presentation, status references) and sub-target edits (underbarrels).
    // Format 12: additional files packaged into the exported ZIP (ModProject.PackagedFiles) and HD2Arsenal presentation (ModProject.Arsenal);
    // older ModBuilder versions reject the format instead of dropping either when they save.
    private static bool Composition(string fieldType) => AuthoredTypes.Composition.Contains(fieldType);
    public static int RequiredFormat(ModProject p) => p.PackagedFiles is { Count: > 0 } || p.Arsenal != null ? 12 : p.CustomLua != null || p.AttackOutputChanges is { Count: > 0 } || p.OutputRowChanges is { Count: > 0 }
        || p.EntityChanges.Any(c => c.Linked != null || c.FieldType == Metadata.WeaponCapability.StatusReference)
        || p.WeaponChanges.Any(c => Composition(c.FieldType) || c.Subweapon != null) || p.SupportChanges.Any(c => Composition(c.FieldType))
        || p.CompositionChanges.Any(c => c.Scalar is { } s && Composition(s.FieldType)) ? 11 :
        p.EntityChanges.Any(c => Generation.EntityChangeService.IsEnemy(c.Resource) || c.SharedConsumers != null) ? 10 :
        p.EntityChanges.Any(c => c.Resource == ThrowableAuthoringReader.Resource || c.Effect != null) ? 9 :
        p.WeaponChanges.Any(c => c.FieldType == Metadata.WeaponCapability.FireModeSet || c.EffectAcknowledgement != null)
        || p.SupportChanges.Any(c => c.FieldType == Metadata.WeaponCapability.FireModeSet)
        || p.StratagemChanges.Any(c => c.FieldType == Metadata.StratagemUses.Type || c.EffectAcknowledgement != null)
        || p.EntityChanges.Any(c => c.Resource is "vehicle_weapon" or "pod_rack" || c.Attack != null || c.Slot != null) ? 8 : 1;
    public static void Validate(ModProject p)
    {
        if (p.FormatVersion is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12) || p.Id == Guid.Empty) throw new InvalidDataException(CoreText.Get("Messages.Project.UnsupportedFormat"));
        // Additional packaged files: their shape only. A destination that is not a safe ZIP path is reported where it is edited and refused at
        // export (Generation.PackagedFiles), so a hand-edited project still opens.
        if (p.PackagedFiles is { } packaged && (packaged.Count > Generation.PackagedFiles.MaxFiles || packaged.Any(f => f == null || f.Source == null || f.Destination == null
                || f.Source.Length > 1024 || f.Destination.Length > 1024 || f.Source.Any(char.IsControl))))
            throw new InvalidDataException(CoreText.Get("Messages.Project.InvalidPackagedFiles"));
        // Arsenal presentation: its shape only; what Arsenal would not show as written is reported on the Export page and refused at export.
        if (p.Arsenal is { } arsenal && (arsenal.Description?.Length > 16 * Generation.Arsenal.MaxDescription
                || arsenal.Icon is { } icon && (icon.Length > 1024 || icon.Any(char.IsControl))))
            throw new InvalidDataException(CoreText.Get("Messages.Project.InvalidArsenal"));
        if (p.CustomLua is { } lua && (lua.Source == null || lua.Source.Length > CustomLuaSettings.MaxLength || lua.Source.Contains('\0')))
            throw new InvalidDataException(CoreText.Get("Messages.Project.InvalidCustomLua"));
        // Attack outputs: semantic weapon / role / output identities and the published opt-ins only.
        // Player hosts save no kind; support hosts name a support weapon, mounted hosts the published "<vehicle> / <mount>" key (component only).
        if (p.AttackOutputChanges is { } outputs && (outputs.Count > 500 || outputs.Select(c => c.Id).Distinct().Count() != outputs.Count
            || outputs.GroupBy(c => (c.Kind, c.Weapon, c.AttackRole)).Any(g => g.Count() > 1)
            || outputs.Any(c => c.Id == Guid.Empty || string.IsNullOrWhiteSpace(c.Weapon) || c.Weapon.Length > 256 || !Regex.IsMatch(c.AttackRole, @"\A[a-z][a-z_0-9]{0,63}\z")
                || c.Mechanism is not ("component" or "ammunition") || !Regex.IsMatch(c.Output, @"\Aoutput/v1/projectile/[a-z0-9-]{1,128}\z") || c.OutputName.Length > 256
                || c.Acknowledgements.Any(a => a is not ("allow_shared" or "allow_unverified_effect" or "allow_unverified_reference")) || !Regex.IsMatch(c.Evidence, @"\A[a-f0-9]{64}\z")
                || c.Group.Length > 120 || c.Notes?.Length > 4000
                || c.HostKind is not (null or AttackOutputChange.SupportHost or AttackOutputChange.VehicleHost)
                || c.HostKind != null && c.Mechanism != "component"
                || c.HostKind == AttackOutputChange.VehicleHost && !Regex.IsMatch(c.Weapon, @"\A[^\p{C}]{1,200} / [a-z][a-z_0-9]{0,63}\z"))))
            throw new InvalidDataException(CoreText.Get("Messages.Project.InvalidAttackOutputs"));
        foreach (var c in p.AttackOutputChanges ?? []) SemVersion.Parse(c.BaselineSdkVersion);
        // Row writes: a catalogued row, a Runtime row field, and semantic values ("none", "<output>#<slot>", or a native label / icon name).
        if (p.OutputRowChanges is { } rows && (rows.Count > 500 || rows.Select(c => c.Id).Distinct().Count() != rows.Count
            || rows.GroupBy(c => (c.Output, c.Field)).Any(g => g.Count() > 1)
            || rows.Any(c => c.Id == Guid.Empty || !Regex.IsMatch(c.Output, @"\Aoutput/v1/projectile/[a-z0-9-]{1,128}\z") || c.OutputName.Length > 256
                || !Generation.OutputRowChangeService.Fields.Contains(c.Field) || !RowValue(c.Field, c.Expect) || !RowValue(c.Field, c.Value) || c.Expect == c.Value
                || c.ValueName?.Length > 256 || c.Acknowledgements.Any(a => a is not ("allow_shared" or "allow_unverified_effect"))
                || !Regex.IsMatch(c.Evidence, @"\A[a-f0-9]{64}\z") || c.Group.Length > 120 || c.Notes?.Length > 4000)))
            throw new InvalidDataException(CoreText.Get("Messages.Project.InvalidRowChanges"));
        foreach (var c in p.OutputRowChanges ?? []) SemVersion.Parse(c.BaselineSdkVersion);
        if (string.IsNullOrWhiteSpace(p.DisplayName) || p.DisplayName.Length > 120 || string.IsNullOrWhiteSpace(p.Author) || p.Author.Length > 120)
            throw new InvalidDataException(CoreText.Get("Messages.Project.NameAuthorRequired"));
        ValidateResource(p.ResourceId);
        if (p.ManagerGuid != ManagerGuid(p.ResourceId)) throw new InvalidDataException(CoreText.Get("Messages.Project.ManagerGuidMismatch"));
        if (!Regex.IsMatch(p.Version, @"\A(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\z")) throw new InvalidDataException(CoreText.Get("Messages.Project.VersionFormat"));
        SemVersion.Parse(p.Version); SemVersion.Parse(p.SdkVersion);
        if (p.RuntimeApi != 1 || p.Changes == null || p.Changes.Count > 1000 || p.Description.Length > 8000) throw new InvalidDataException(CoreText.Get("Messages.Project.InvalidData"));
        if (p.Changes.Select(c => c.Id).Distinct().Count() != p.Changes.Count) throw new InvalidDataException(CoreText.Get("Messages.Project.DuplicateChangeIds"));
        if (p.WeaponChanges == null || p.WeaponChanges.Count > 1000 || p.WeaponChanges.Any(c => c.Id == Guid.Empty || string.IsNullOrWhiteSpace(c.Weapon) || c.Weapon.Length > 256 || string.IsNullOrWhiteSpace(c.SemanticFieldId) || c.SemanticFieldId.Length > 128 || c.Group.Length > 120 || c.Notes?.Length > 4000 || c.AcknowledgedAffectedWeapons == null
            || !(c.FieldType == Metadata.WeaponCapability.FireModeSet ? ModeList(c.ExpectedValue) && ModeList(c.DesiredValue)
                : Composition(c.FieldType) ? CompositionValue(c.FieldType, c.ExpectedValue, true) && CompositionValue(c.FieldType, c.DesiredValue, false) : Scalar(c.ExpectedValue) && Scalar(c.DesiredValue))
            || c.Subweapon != null && !Regex.IsMatch(c.Subweapon, @"\A[a-z][a-z_]{0,31}\z")
            || c.EffectAcknowledgement != null && !Regex.IsMatch(c.EffectAcknowledgement, @"\A[a-f0-9]{64}\z"))) throw new InvalidDataException(CoreText.Get("Messages.Project.InvalidWeaponOverrides"));
        if (p.WeaponChanges.Select(c => c.Id).Distinct().Count() != p.WeaponChanges.Count) throw new InvalidDataException(CoreText.Get("Messages.Project.DuplicateWeaponChangeIds"));
        if (p.ProjectileChanges == null || p.ProjectileChanges.Count > 1000 || p.ProjectileChanges.Any(c => c.Id == Guid.Empty || string.IsNullOrWhiteSpace(c.Weapon) || c.Weapon.Length > 256
            || !Regex.IsMatch(c.AttackRole, "\\A[a-z][a-z_0-9]{0,63}\\z") || c.SemanticFieldId != "attack.projectile" || c.ExpectedProjectile != new ProjectileReference(c.Weapon, c.AttackRole)
            || c.ReplacementProjectile == null || string.IsNullOrWhiteSpace(c.ReplacementProjectile.Weapon) || c.ReplacementProjectile.Weapon.Length > 256
            || !Regex.IsMatch(c.ReplacementProjectile.AttackRole, "\\A[a-z][a-z_0-9]{0,63}\\z") || c.Group.Length > 120 || c.Notes?.Length > 4000
            || !Regex.IsMatch(c.ExpectedEvidence, "\\A[a-f0-9]{64}\\z") || !Regex.IsMatch(c.ReplacementEvidence, "\\A[a-f0-9]{64}\\z"))) throw new InvalidDataException(CoreText.Get("Messages.Project.InvalidProjectileOverrides"));
        if (p.ProjectileChanges.Select(c => c.Id).Distinct().Count() != p.ProjectileChanges.Count) throw new InvalidDataException(CoreText.Get("Messages.Project.DuplicateProjectileChangeIds"));
        foreach (var c in p.ProjectileChanges) SemVersion.Parse(c.BaselineSdkVersion);
        if (p.SupportChanges == null || p.SupportChanges.Count > 2000 || p.SupportApprovals == null || p.SupportApprovals.Count > 2000
            || p.SupportChanges.Select(c => c.InstanceKey).Distinct().Count() != p.SupportChanges.Count || p.SupportChanges.Select(c => c.Id).Distinct().Count() != p.SupportChanges.Count
            || p.SupportChanges.Any(c => c.Id == Guid.Empty || c.InstanceKey.Length > 512 || !c.InstanceKey.StartsWith("support-field/v1/", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(c.Weapon) || c.Weapon.Length > 256 || c.SemanticFieldId.Length > 128 || c.Group.Length > 120 || c.Notes?.Length > 4000
                || !Regex.IsMatch(c.CapabilityEvidence, @"\A[a-f0-9]{64}\z") || c.EffectAcknowledgement != null && !Regex.IsMatch(c.EffectAcknowledgement, @"\A[a-f0-9]{64}\z")
                || !(c.FieldType == Metadata.WeaponCapability.FireModeSet ? ModeList(c.ExpectedValue) && ModeList(c.DesiredValue)
                    : Composition(c.FieldType) ? CompositionValue(c.FieldType, c.ExpectedValue, true) && CompositionValue(c.FieldType, c.DesiredValue, false)
                    : c.FieldType is "number" or "integer" or "boolean" && NumberOrBool(c.ExpectedValue) && NumberOrBool(c.DesiredValue)))) throw new InvalidDataException(CoreText.Get("Messages.Project.InvalidSupportOverrides"));
        foreach (var c in p.SupportChanges) SemVersion.Parse(c.BaselineSdkVersion);
        foreach (var a in p.SupportApprovals) if (a.Key.Length > 256 || !a.Key.StartsWith("support-scope/v1/", StringComparison.Ordinal) || !Regex.IsMatch(a.Value, @"\A[a-f0-9]{64}\z")) throw new InvalidDataException(CoreText.Get("Messages.Project.InvalidSupportApproval"));
        if (p.StratagemChanges == null || p.StratagemApprovals == null || p.StratagemChanges.Count > 2000 || p.StratagemApprovals.Count > 2000
            || p.StratagemChanges.Select(c => c.InstanceKey).Distinct().Count() != p.StratagemChanges.Count
            || p.StratagemChanges.Select(c => c.Id).Distinct().Count() != p.StratagemChanges.Count
            || p.StratagemChanges.Any(c => c.Id == Guid.Empty || !ValidStratagemTarget(c) || string.IsNullOrWhiteSpace(c.Stratagem)
                || c.Stratagem.Length > 256 || !c.InstanceKey.StartsWith("stratagem:", StringComparison.Ordinal) || c.InstanceKey.Length > 512
                || c.SemanticFieldId.Length > 128
                || c.Group.Length > 120 || c.Notes?.Length > 4000
                || !Regex.IsMatch(c.CapabilityEvidence, @"\A[a-f0-9]{64}\z")
                || c.EffectAcknowledgement != null && !Regex.IsMatch(c.EffectAcknowledgement, @"\A[a-f0-9]{64}\z")
                // 0.26.0 mission uses: an integer count or Runtime's 'unlimited'.
                || !(c.FieldType == Metadata.StratagemUses.Type ? Uses(c.ExpectedValue) && Uses(c.DesiredValue)
                    : c.FieldType is "number" or "integer" or "boolean" && NumberOrBool(c.ExpectedValue) && NumberOrBool(c.DesiredValue))))
            throw new InvalidDataException(CoreText.Get("Messages.Project.InvalidStratagemOverrides"));
        foreach (var c in p.StratagemChanges) SemVersion.Parse(c.BaselineSdkVersion);
        foreach (var a in p.StratagemApprovals) if (a.Key.Length > 256 || !Regex.IsMatch(a.Value, @"\A[a-f0-9]{64}\z")) throw new InvalidDataException(CoreText.Get("Messages.Project.InvalidStratagemApproval"));
        // Format 6: vehicle/backpack (0.23.0), magazine attachment (0.23.1) and booster (0.24.0) changes. Targets are published names and zone_N / slot_N identities; mount values are published semantic IDs.
        static bool Slot(string? s, string prefix) => s != null && Regex.IsMatch(s, @"\A" + prefix + @"_[0-9]{1,3}\z");
        if (p.EntityChanges == null || p.EntityApprovals == null || p.EntityChanges.Count > 4000 || p.EntityApprovals.Count > 2000
            || p.EntityChanges.Select(c => c.Id).Distinct().Count() != p.EntityChanges.Count
            || p.EntityChanges.Select(c => c.InstanceKey).Distinct().Count() != p.EntityChanges.Count
            || p.EntityChanges.Any(c => c.Id == Guid.Empty || string.IsNullOrWhiteSpace(c.Entity) || c.Entity.Length > 256 || c.SemanticFieldId.Length > 128
                || !c.InstanceKey.StartsWith(c.Resource switch { "weapon_attachment" => "attachment:", "vehicle_weapon" => "vehicle-field/v1/", _ => c.Resource + ":" }, StringComparison.Ordinal) || c.InstanceKey.Length > 512
                || !((c.Resource, c.Path) switch
                {
                    ("vehicle", "entity") => c.Zone == null && c.Mount == null,
                    ("vehicle", "damage_zone") => Slot(c.Zone, "zone") && c.Mount == null,
                    ("vehicle", "mount") => Slot(c.Mount, "slot") && c.Zone == null && c.FieldType == EntityField.ReferenceType,
                    ("backpack", "backpack") => c.Zone == null && c.Mount == null && c.Linked == null,
                    // 1.4.0: a backpack's own damage zone, or one of the entity it deploys or projects (a Guard Dog drone, the SH-51 energy shield).
                    ("backpack", "damage_zone") => Slot(c.Zone, "zone") && c.Mount == null && (c.Linked == null || BackpackLinkedEntity.Kinds.Contains(c.Linked)),
                    ("backpack", "linked") => c.Zone == null && c.Mount == null && c.Linked != null && BackpackLinkedEntity.Kinds.Contains(c.Linked),
                    // 0.23.1 magazine attachment definitions: Entity is the published attachment semantic ID.
                    // 0.26.0 adds reload duration and ergonomics modifier (numbers) to the four integer ammo fields.
                    ("weapon_attachment", "magazine") => c.Zone == null && c.Mount == null && c.FieldType is "integer" or "number"
                        && Regex.IsMatch(c.Entity, @"\Aweapon-attachment/v1/magazine/[a-z0-9-]{1,96}/[0-9a-f]{16}\z"),
                    // 0.24.0+ boosters: Entity is the published booster name; only its reviewed sub-targets carry fields (more targets from 0.25.0).
                    ("booster", var boosterPath) when BoosterAuthoringReader.Paths.Contains(boosterPath) => c.Zone == null && c.Mount == null && BoosterAuthoringReader.ValidName(c.Entity),
                    // 0.26.0 vehicle weapons: Entity is the published weapon key; the weapon itself, or one of its attack objects.
                    ("vehicle_weapon", "weapon") => c.Zone == null && c.Mount == null && c.Slot == null && c.Attack == null && VehicleWeaponReader.ValidKey(c.Entity),
                    ("vehicle_weapon", "projectile_reference" or "explosion" or "attack") => c.Zone == null && c.Mount == null && c.Slot == null
                        && c.Attack != null && Regex.IsMatch(c.Attack, @"\A[a-z][a-z_0-9]{0,63}\z") && VehicleWeaponReader.ValidKey(c.Entity),
                    // 0.26.0 drop-pod racks: Entity is the published rack name; spawn count on the rack, payload items on slots 1-4.
                    ("pod_rack", "rack") => c.Zone == null && c.Mount == null && c.Slot == null && c.Attack == null && PodPayloadReader.ValidName(c.Entity) && c.FieldType == "integer",
                    // 0.27.0 throwables: Entity is the published throwable name; a published target, and a status effect's key.
                    ("throwable", var throwablePath) when ThrowableAuthoringReader.Accessors.ContainsKey(throwablePath) => c.Zone == null && c.Mount == null && c.Slot == null && c.Attack == null
                        && ThrowableAuthoringReader.ValidName(c.Entity) && (throwablePath == "status_effect" ? c.Effect != null && ThrowableAuthoringReader.ValidKey(c.Effect) : c.Effect == null),
                    ("pod_rack", "slot") => c.Zone == null && c.Mount == null && c.Slot is >= 1 and <= 4 && c.Attack == null && PodPayloadReader.ValidName(c.Entity) && c.FieldType == EntityField.PickupType,
                    // Enemies and enemy structures: Entity is the class semantic ID; the class, one damage zone (zone_N) or one attack row (slot_N...).
                    (EnemyAuthoringReader.Enemy or EnemyAuthoringReader.Structure, var enemyPath) => c.Mount == null && c.Slot == null && c.Effect == null
                        && EnemyAuthoringReader.ValidSemanticId(c.Entity) && c.FieldType is "integer" or "number" && enemyPath switch
                        {
                            EnemyAuthoringReader.EntityPath => c.Zone == null && c.Attack == null,
                            EnemyAuthoringReader.ZonePath => EnemyAuthoringReader.ValidZone(c.Zone) && c.Attack == null,
                            EnemyAuthoringReader.AttackPath => c.Zone == null && EnemyAuthoringReader.ValidAttack(c.Attack),
                            _ => false,
                        },
                    _ => false,
                })
                || c.SharedConsumers != null && (!Generation.EntityChangeService.IsEnemy(c.Resource) || c.SharedConsumers.Length is 0 or > 256 || c.SharedConsumers.Any(n => string.IsNullOrWhiteSpace(n) || n.Length > 256))
                // 1.4.0: a linked entity only on a backpack field, and a Guard Dog drone ("drone") only on a mounted-weapon field.
                || c.Linked != null && !(c.Resource == "backpack" && c.Path is "linked" or "damage_zone" || c.Resource == "vehicle_weapon" && c.Linked == BackpackLinkedEntity.Drone)
                || (c.FieldType switch
                {
                    EntityField.ReferenceType => c.ExpectedValue.ValueKind != System.Text.Json.JsonValueKind.String || c.DesiredValue.ValueKind != System.Text.Json.JsonValueKind.String
                        || !Regex.IsMatch(c.DesiredValue.GetString()!, @"\Amounted-weapon/v1/[a-z0-9-]{1,96}/[0-9a-f]{16}\z"),
                    EntityField.PickupType => !PodPayloadReader.ValidValue(c.ExpectedValue) || !PodPayloadReader.ValidValue(c.DesiredValue),
                    // 1.4.0: a mounted-weapon status slot holds a status key or 'none'.
                    Metadata.WeaponCapability.StatusReference => c.Resource != "vehicle_weapon" || !Generation.EntityScalar.ValidStatusKey(c.ExpectedValue) || !Generation.EntityScalar.ValidStatusKey(c.DesiredValue),
                    _ => c.FieldType is not ("integer" or "number") || c.ExpectedValue.ValueKind != System.Text.Json.JsonValueKind.Number || c.DesiredValue.ValueKind != System.Text.Json.JsonValueKind.Number,
                })
                || c.Group.Length > 120 || c.Notes?.Length > 4000 || !Regex.IsMatch(c.CapabilityEvidence, @"\A[a-f0-9]{64}\z")
                || c.ReferenceAcknowledgement != null && !Regex.IsMatch(c.ReferenceAcknowledgement, @"\A[a-f0-9]{64}\z")))
            throw new InvalidDataException(CoreText.Get("Messages.Project.InvalidEntityOverrides"));
        foreach (var c in p.EntityChanges) SemVersion.Parse(c.BaselineSdkVersion);
        foreach (var a in p.EntityApprovals) if (a.Key.Length > 256 || !Regex.IsMatch(a.Value, @"\A[a-f0-9]{64}\z")) throw new InvalidDataException(CoreText.Get("Messages.Project.InvalidEntityApproval"));
        if (p.CompositionChanges == null || p.CompositionChanges.Count > 1000 || p.CompositionChanges.Select(c => c.Id).Distinct().Count() != p.CompositionChanges.Count) throw new InvalidDataException(CoreText.Get("Messages.Project.InvalidCompositionChanges"));
        foreach (var c in p.CompositionChanges)
        {
            bool Reference(ProjectileReference? r) => r != null && !string.IsNullOrWhiteSpace(r.Weapon) && r.Weapon.Length <= 256 && Regex.IsMatch(r.AttackRole, @"\A[a-z][a-z_0-9]{0,63}\z");
            bool Explosion(ExplosionReference? r) => r != null && (r.IsNone ? r.Phase == null : Reference(r.Projectile) && r.Phase is "impact" or "expiry");
            if (c.Id == Guid.Empty || !Reference(new(c.Weapon, c.AttackRole)) || !Reference(c.Target) || c.Kind is not ("projectile" or "explosion" or "terminal")
                || (c.Kind == "projectile" ? c.Phase != null : c.Phase is not ("impact" or "expiry")) || c.Group.Length > 120 || c.Notes?.Length > 4000
                || (c.Kind == "terminal" ? c.Scalar != null || !Explosion(c.ExpectedExplosion) || !Explosion(c.DesiredExplosion) : c.Scalar == null)
                || c.Kind == "explosion" && !Explosion(c.ExplosionTarget)) throw new InvalidDataException(CoreText.Get("Messages.Project.InvalidCompositionOverride"));
            SemVersion.Parse(c.BaselineSdkVersion);
        }
        if (!Path.IsPathFullyQualified(p.ExportDirectory)) throw new InvalidDataException(CoreText.Get("Messages.Project.ExportDirectoryAbsolute"));
    }
}

public interface IProjectStore
{
    Task<IReadOnlyList<ProjectSummary>> ListAsync();
    Task<ModProject> LoadAsync(Guid id);
    Task SaveAsync(ModProject project);
    Task RemoveFromLibraryAsync(Guid id);
    Task<ModProject> ImportAsync(string file);
}

public sealed class JsonProjectStore(AppPaths paths) : IProjectStore
{
    private readonly SemaphoreSlim gate = new(1);
    private sealed class Library { public int FormatVersion { get; set; } = 1; public List<ProjectSummary> Projects { get; set; } = []; }
    private async Task<Library> ReadLibraryAsync()
    {
        var library = File.Exists(paths.Library) ? await JsonStorage.ReadAsync<Library>(paths.Library) : new Library();
        if (library.FormatVersion != 1 || library.Projects == null) throw new InvalidDataException(CoreText.Get("Messages.Project.UnsupportedLibrary"));
        return library;
    }
    public async Task<IReadOnlyList<ProjectSummary>> ListAsync() => (await ReadLibraryAsync()).Projects.OrderByDescending(p => p.ModifiedAt).ToArray();
    public async Task<ModProject> LoadAsync(Guid id)
    {
        var project = await JsonStorage.ReadAsync<ModProject>(paths.ProjectFile(id));
        ProjectIdentity.Validate(project);
        if (project.Id != id) throw new InvalidDataException(CoreText.Get("Messages.Project.IdentityMismatch"));
        return project;
    }
    public async Task SaveAsync(ModProject project)
    {
        ProjectIdentity.Validate(project);
        await gate.WaitAsync();
        try
        {
            var library = await ReadLibraryAsync();
            if (library.Projects.Any(p => p.Id != project.Id && p.ResourceId.Equals(project.ResourceId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException(CoreText.Get("Messages.Project.ResourceIdTaken"));
            project.ModifiedAt = DateTimeOffset.UtcNow;
            await JsonStorage.WriteAtomicAsync(paths.ProjectFile(project.Id), project);
            library.Projects.RemoveAll(p => p.Id == project.Id);
            library.Projects.Add(new(project.Id, project.DisplayName, project.ResourceId, project.SdkVersion, project.ModifiedAt));
            await JsonStorage.WriteAtomicAsync(paths.Library, library);
        }
        finally { gate.Release(); }
    }
    public async Task RemoveFromLibraryAsync(Guid id)
    {
        await gate.WaitAsync();
        try { var library = await ReadLibraryAsync(); library.Projects.RemoveAll(p => p.Id == id); await JsonStorage.WriteAtomicAsync(paths.Library, library); }
        finally { gate.Release(); }
    }
    public async Task<ModProject> ImportAsync(string file)
    {
        var project = await JsonStorage.ReadAsync<ModProject>(file);
        ProjectIdentity.Validate(project);
        // Keep one managed project root. Opening a removed project re-registers its file.
        if (File.Exists(paths.ProjectFile(project.Id)) && !Path.GetFullPath(file).Equals(paths.ProjectFile(project.Id), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(CoreText.Get("Messages.Project.AlreadyStored"));
        await SaveAsync(project); return project;
    }
}

public interface IProjectService
{
    Task<ModProject> CreateAsync(CreateProjectRequest request, SdkMetadata sdk);
    Task<ModProject> DuplicateAsync(ModProject source, CreateProjectRequest request);
    Task RenameAsync(ModProject project, string name);
}
public sealed class ProjectService(IProjectStore store, AppPaths paths) : IProjectService
{
    public async Task<ModProject> CreateAsync(CreateProjectRequest request, SdkMetadata sdk)
    {
        var project = New(request, sdk.Version, sdk.ApiVersion);
        await store.SaveAsync(project); return project;
    }
    public async Task<ModProject> DuplicateAsync(ModProject source, CreateProjectRequest request)
    {
        var project = New(request, source.SdkVersion, source.RuntimeApi);
        project.Changes = System.Text.Json.JsonSerializer.Deserialize<List<ModChange>>(System.Text.Json.JsonSerializer.Serialize(source.Changes))!;
        foreach (var change in project.Changes) change.Id = Guid.NewGuid();
        project.WeaponChanges = System.Text.Json.JsonSerializer.Deserialize<List<WeaponChange>>(System.Text.Json.JsonSerializer.Serialize(source.WeaponChanges))!;
        foreach (var change in project.WeaponChanges) change.Id = Guid.NewGuid();
        project.ProjectileChanges = System.Text.Json.JsonSerializer.Deserialize<List<ProjectileChange>>(System.Text.Json.JsonSerializer.Serialize(source.ProjectileChanges))!;
        foreach (var change in project.ProjectileChanges) change.Id = Guid.NewGuid();
        project.CompositionChanges = System.Text.Json.JsonSerializer.Deserialize<List<CompositionChange>>(System.Text.Json.JsonSerializer.Serialize(source.CompositionChanges))!;
        foreach (var change in project.CompositionChanges) change.Id = Guid.NewGuid();
        project.SupportChanges = source.SupportChanges.Select(c => c with { Id = Guid.NewGuid() }).ToList();
        project.SupportApprovals = new(source.SupportApprovals);
        project.StratagemChanges = source.StratagemChanges.Select(c => c with { Id = Guid.NewGuid() }).ToList();
        project.StratagemApprovals = new(source.StratagemApprovals);
        project.EntityChanges = source.EntityChanges.Select(c => c with { Id = Guid.NewGuid() }).ToList();
        project.EntityApprovals = new(source.EntityApprovals);
        // Format 11: the saved custom Lua text (the copy's own src/addon.lua is created from it when it opens) and attack outputs.
        project.CustomLua = source.CustomLua;
        project.AttackOutputChanges = source.AttackOutputChanges?.Select(c => c with { Id = Guid.NewGuid() }).ToList();
        project.OutputRowChanges = source.OutputRowChanges?.Select(c => c with { Id = Guid.NewGuid() }).ToList();
        // Format 12: the copy packages the same additional files (the same files on disk) and keeps the Arsenal presentation.
        project.PackagedFiles = source.PackagedFiles?.Select(f => new PackagedFile { Source = f.Source, Destination = f.Destination }).ToList();
        project.Arsenal = source.Arsenal is { } arsenal ? new() { Description = arsenal.Description, Icon = arsenal.Icon } : null;
        if (project.PackagedFiles != null || project.Arsenal != null) project.FormatVersion = Math.Max(project.FormatVersion, ProjectIdentity.RequiredFormat(project));
        await store.SaveAsync(project); return project;
    }
    public async Task RenameAsync(ModProject project, string name)
    {
        var old = project.DisplayName; project.DisplayName = name;
        try { await store.SaveAsync(project); } catch { project.DisplayName = old; throw; }
    }
    private ModProject New(CreateProjectRequest r, string sdkVersion, int api) => new()
    {
        DisplayName = r.DisplayName.Trim(), Author = r.Author.Trim(), ResourceId = r.ResourceId.Trim(),
        ManagerGuid = ProjectIdentity.ManagerGuid(r.ResourceId.Trim()), Version = r.Version.Trim(), Description = r.Description.Trim(),
        SdkVersion = sdkVersion, RuntimeApi = api, ExportDirectory = paths.Exports
    };
}
