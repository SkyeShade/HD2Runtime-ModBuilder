using System.Text.Json;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.CustomContent;
using HD2RuntimeGUI.Core.Models;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// ModBuilder's custom stratagem engine against HD2Runtime's own SDK tools (sdk/tools/custom_stratagem_project.py, HD2Runtime 0.30.0). The
// oracle (Fixtures/custom-content/oracle/runtime-oracle.json, tools/custom-content/make-runtime-oracle.py) records what the SDK's validator and
// compiler say about every Runtime example, every ModBuilder template and about 140 projects that each break one rule: the ports must say exactly
// the same, in the same order, byte for byte, and every example and template must round-trip through ModBuilder's model into the registration
// the SDK writes.
public sealed class CustomStratagemConformanceTests
{
    private static readonly Runtime030Target Target = (Runtime030Target)CustomContentTargets.Current;
    private static readonly JsonObject Oracle = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "custom-content", "oracle", "runtime-oracle.json")))!;
    private static readonly JsonDocument Schema = JsonDocument.Parse(CustomContentTargets.BundledSchema());
    public static TheoryData<string> Examples() => [.. ((JsonArray)Oracle["examples"]!).Select(e => e!["name"]!.GetValue<string>())];
    public static TheoryData<string> Cases() => [.. ((JsonArray)Oracle["cases"]!).Select(e => e!["name"]!.GetValue<string>())];
    public static TheoryData<string> Templates() => [.. ((JsonArray)Oracle["templates"]!).Select(e => e!["name"]!.GetValue<string>())];
    public static TheoryData<string, string> Projects()
    {
        var data = new TheoryData<string, string>();
        foreach (var list in new[] { "examples", "templates" })
            foreach (var e in (JsonArray)Oracle[list]!) data.Add(list, e!["name"]!.GetValue<string>());
        return data;
    }
    private static JsonObject Entry(string list, string name) => (JsonObject)((JsonArray)Oracle[list]!).Single(e => e!["name"]!.GetValue<string>() == name)!;
    private static string[] Expected(JsonObject entry) => [.. ((JsonArray)entry["problems"]!).Select(p => p!.GetValue<string>())];
    /// <summary>A Runtime example's (first) stratagem read into ModBuilder's model, as a template would be.</summary>
    private static CustomStratagem Example(string name) => RuntimeProjectForm.Read((JsonObject)Entry("examples", name)["project"]!["stratagems"]![0]!.DeepClone());
    private static string[] Actual(JsonObject entry) => [.. RuntimeProjectValidator.Validate(entry["project"]!.DeepClone(), Schema.RootElement).Select(p => p.ToString())];

    [Fact] public void The_oracle_was_made_from_the_bundled_schema()
    {
        Assert.Equal(Target.Schema.Sha256, Oracle["sdk_schema_sha256"]!.GetValue<string>());
        Assert.Equal(13, ((JsonArray)Oracle["examples"]!).Count);
        Assert.True(((JsonArray)Oracle["cases"]!).Count >= 130);
    }

    // The oracle's templates are the bundled ones, JSON for JSON: regenerate it (make-runtime-oracle.py) whenever a template changes.
    [Fact] public void The_oracle_covers_exactly_the_bundled_templates()
    {
        var oracle = ((JsonArray)Oracle["templates"]!).ToDictionary(e => e!["name"]!.GetValue<string>(), e => e!["project"]!.ToJsonString());
        Assert.Equal(CustomTemplates.Examples.Select(e => e.Name).Order(StringComparer.Ordinal), oracle.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, project) in CustomTemplates.Examples) Assert.Equal(oracle[name], project.ToJsonString());
        Assert.Empty((JsonArray)Oracle["templates_together"]!);
        // The two HD2Runtime examples among them are the SDK's fixtures, byte for byte.
        foreach (var name in CustomTemplates.RuntimeExampleNames) Assert.Equal(Entry("examples", name)["project"]!.ToJsonString(), oracle[name]);
    }

    [Theory, MemberData(nameof(Templates))]
    public void Every_template_is_valid_and_compiles_to_the_SDKs_addon_byte_for_byte(string name)
    {
        var entry = Entry("templates", name);
        Assert.Empty(Expected(entry));
        Assert.Empty(Actual(entry));
        Assert.Equal(entry["addon"]!.GetValue<string>(), RuntimeRegistrationLua.Addon((JsonObject)entry["project"]!.DeepClone()));
    }

    [Theory, MemberData(nameof(Cases))]
    public void The_validator_reports_exactly_what_the_Runtime_SDK_reports(string name)
    {
        var entry = Entry("cases", name);
        if (entry["crash"] is { } crash)
        {
            // The SDK's validator fails on this project (its own defect); the port reports the problem it had found instead of failing.
            var problems = Actual(entry);
            Assert.NotEmpty(problems);
            Assert.Contains(problems, p => p.Contains("unsupported field", StringComparison.Ordinal));
            Assert.Contains("KeyError", crash.GetValue<string>());
            return;
        }
        Assert.Equal(Expected(entry), Actual(entry));
    }

    [Theory, MemberData(nameof(Examples))]
    public void Every_Runtime_example_is_valid_and_compiles_to_the_SDKs_addon_byte_for_byte(string name)
    {
        var entry = Entry("examples", name);
        Assert.Empty(Actual(entry));
        Assert.Equal(entry["addon"]!.GetValue<string>(), RuntimeRegistrationLua.Addon((JsonObject)entry["project"]!.DeepClone()));
    }

    [Theory, MemberData(nameof(Cases))]
    public void Every_valid_case_compiles_to_the_SDKs_addon_byte_for_byte(string name)
    {
        var entry = Entry("cases", name);
        if (entry["addon"] is not JsonValue addon) return;
        Assert.Equal(addon.GetValue<string>(), RuntimeRegistrationLua.Addon((JsonObject)entry["project"]!.DeepClone()));
    }

    // A template (and any Runtime example, imported) is read into ModBuilder's model: written back into the project form and compiled, it is
    // the SDK's own registration of that project, field for field (the icon source path aside, which the project form always writes).
    [Theory, MemberData(nameof(Projects))]
    public void Every_example_and_template_round_trips_through_the_model_into_the_SDKs_registration(string list, string name)
    {
        var entry = Entry(list, name);
        foreach (var original in ((JsonArray)entry["project"]!["stratagems"]!).OfType<JsonObject>())
        {
            var ignored = new List<string>();
            var s = RuntimeProjectForm.Read((JsonObject)original.DeepClone(), ignored);
            Assert.Empty(ignored);
            var form = Target.ProjectForm(s);
            Assert.Equal(RuntimeRegistrationLua.Register(original), RuntimeRegistrationLua.Register(form));
            Assert.Empty(RuntimeProjectValidator.Validate(new JsonObject { ["format"] = RuntimeProjectForm.Format, ["stratagems"] = new JsonArray(form) }, Schema.RootElement));
        }
    }

    // What the build writes for a custom stratagem: its register call's fields are exactly the SDK compiler's lines (indented inside ModBuilder's
    // isolating wrapper), and the target finds no error in any template.
    [Fact] public void The_build_registers_every_template_with_the_SDK_compilers_field_lines_and_no_error()
    {
        foreach (var template in CustomTemplates.Stratagems)
        {
            var s = template.Create();
            s.Icon = s.Id;
            var content = new CustomContentSection { Stratagems = [s], Images = [Image(s.Id)] };
            foreach (var p in new[] { s.Delivery.Expendable?.Presentation, s.Delivery.Variant?.Presentation })
                if (p is { IconMode: DeliveredPresentation.Custom, Icon: { } icon } && content.Images.All(i => i.Id != icon)) content.Images.Add(Image(icon));
            var errors = Target.Validate(content, Project(content), null).Where(d => d.Severity == CustomSeverity.Error).Select(d => d.Field + ": " + d.Message).ToArray();
            Assert.True(errors.Length == 0, template.Key + ": " + string.Join(" | ", errors));
            var registration = Target.Registration(s).Split('\n');
            var lines = RuntimeRegistrationLua.FieldLines(Target.ProjectForm(s));
            Assert.Equal(lines.Select(l => "    " + l), registration.Skip(2).Take(lines.Count));
            Assert.Equal("register_custom_stratagem(" + RuntimeRegistrationLua.LuaString(s.Id) + ", function()", registration[0]);
        }
    }

    // The one place the port departs from the SDK's validator: a carrier group whose beacon is "any" (orbital, any) implies that beacon. The
    // SDK's validator refuses {group = 'orbital'} for an orbital or a Pelican ("needs a … beacon"); HD2Runtime registers it with beacon 'any'
    // (checked in its offline world: group orbital / any, group_source requested, alloc_policy.beacon any).
    [Theory]
    [InlineData("GasBarrageExample", "orbital")]
    [InlineData("GasBarrageExample", "any")]
    [InlineData("PelicanCannonExample", "orbital")]
    [InlineData("PelicanCannonExample", "any")]
    public void A_group_whose_beacon_is_any_needs_no_beacon_as_the_Runtime_registers_it(string example, string group)
    {
        var project = (JsonObject)Entry("examples", example)["project"]!.DeepClone();
        ((JsonObject)project["stratagems"]![0]!)["carrier"] = new JsonObject { ["group"] = group };
        Assert.Empty(RuntimeProjectValidator.Validate(project, Schema.RootElement));
        // A beacon the group contradicts, or a payload the group cannot carry, is still refused.
        var eagle = (JsonObject)Entry("examples", "EagleStunRocketPodsExample")["project"]!.DeepClone();
        ((JsonObject)eagle["stratagems"]![0]!)["carrier"] = new JsonObject { ["group"] = group, ["allow_families"] = new JsonArray("eagle") };
        Assert.Contains(RuntimeProjectValidator.Validate(eagle, Schema.RootElement), p => p.Where.EndsWith(".carrier.group", StringComparison.Ordinal));
        // In the editor: an orbital that takes the Orbitals group is ready.
        var s = Example(example);
        s.Icon = s.Id; s.Carrier = new() { Group = group };
        var content = new CustomContentSection { Stratagems = [s], Images = [Image(s.Id)] };
        Assert.DoesNotContain(Target.Validate(content, Project(content), null), d => d.Severity == CustomSeverity.Error);
        Assert.Contains("carrier={group='" + group + "'},", Target.Registration(s));
    }

    // max_per_player (r45): the Runtime's Shredder Silo example allows one loadout slot per player. The model keeps it, the form writes it, and
    // the registration carries it after uses, as compile_lua does.
    [Fact] public void A_per_player_limit_survives_the_model_the_form_and_the_registration()
    {
        var s = Example("ShredderSiloExample");
        Assert.Equal((1, 1), (s.MaxPerPlayer, s.Uses));
        Assert.Contains("        uses=1,\n        max_per_player=1,\n", Target.Registration(s));
        s.MaxPerPlayer = null;
        Assert.DoesNotContain("max_per_player", Target.Registration(s));
    }

    [Fact] public void Scripted_stratagems_register_a_Runtime_delivery_and_every_callback_with_its_body()
    {
        var s = new CustomStratagem
        {
            Id = "gas_strike_script", Name = "GAS STRIKE", Description = "A scripted gas barrage.", Icon = "gas", Code = ["up", "up", "left", "right", "down", "down", "up"],
            Delivery = new() { Family = DeliveryFamily.Scripted }, Carrier = new() { Group = "any_red" }, Assets = ["Orbital Gas Strike", "Orbital 120mm HE Barrage"],
            Callbacks = new()
            {
                Setup = "local calls = 0",
                OnActivate = "calls = calls + 1\nctx:barrage({shell = 'Orbital Gas Strike', pattern = 'Orbital 120mm HE Barrage'})",
                OnBeaconLanded = "ctx:log('landed ' .. tostring(calls))",
                ExtraFields = "selection = 'token',",
            },
        };
        var content = new CustomContentSection { Stratagems = [s], Images = [Image("gas")] };
        var errors = Target.Validate(content, Project(content), null).Where(d => d.Severity == CustomSeverity.Error).Select(d => d.Field + ": " + d.Message).ToArray();
        Assert.True(errors.Length == 0, string.Join(" | ", errors));
        var lua = Target.Registration(s);
        Assert.Contains("    local calls = 0\n    return {", lua);
        Assert.Contains("        carrier={group='any_red'},\n        delivery='runtime',\n", lua);
        Assert.Contains("        on_beacon_landed=function(ctx)\n            ctx:log('landed ' .. tostring(calls))\n        end,", lua);
        Assert.Contains("        on_activate=function(ctx)\n            calls = calls + 1\n", lua);
        Assert.True(lua.IndexOf("on_beacon_landed", StringComparison.Ordinal) < lua.IndexOf("on_activate", StringComparison.Ordinal));
        Assert.Contains("        selection = 'token',\n    }\nend)", lua);
        // The whole generated section parses.
        var compiled = Target.Compile(content, Project(content), null!, images: false);
        Assert.Empty(Core.Scripting.LuaScriptAnalyzer.Analyze(compiled.Section, null).Where(d => d.Severity == Core.Scripting.LuaDiagnostic.Error));
    }

    [Fact] public void A_scripted_call_can_use_a_vanilla_support_pod_and_then_see_its_delivery()
    {
        var s = new CustomStratagem
        {
            Id = "gas_eat_script", Name = "GAS EAT", Description = "EAT pods whose rockets gas on impact.", Icon = "eat", Code = ["down", "down", "right", "up", "left", "left"],
            Delivery = new() { Family = DeliveryFamily.Scripted, Scripted = new() { VanillaPod = "EAT-17 Expendable Anti-Tank" } }, Carrier = new() { Beacon = "support" },
            Callbacks = new() { OnDelivered = "for _, w in ipairs(ctx.weapons or {}) do w:set_impact_explosion('Orbital Gas Strike') end" },
        };
        var content = new CustomContentSection { Stratagems = [s], Images = [Image("eat")] };
        Assert.DoesNotContain(Target.Validate(content, Project(content), null), d => d.Severity == CustomSeverity.Error);
        Assert.Contains("        delivery={stratagem='EAT-17 Expendable Anti-Tank'},\n", Target.Registration(s));
        // Without the pod, on_delivered has nothing to see (the Runtime refuses it at registration).
        s.Delivery.Scripted = null;
        Assert.Contains(Target.Validate(content, Project(content), null), d => d.Severity == CustomSeverity.Error && d.Field == "callbacks.on_delivered");
    }

    [Fact] public void Unverified_pod_items_get_their_opt_in_written_and_a_warning_never_a_checkbox()
    {
        var s = new CustomStratagem
        {
            Id = "liberator_drop", Name = "LIBERATOR DROP", Description = "A pod with a spare rifle.", Icon = "drop", Code = ["down", "right", "right", "left", "up", "up", "down"],
            Carrier = new() { Group = "support_pod" },
            Delivery = new() { Family = DeliveryFamily.Pod, Pod = new() { Items = [new() { Item = "player_weapon/AR-23 Liberator" }] } },
        };
        var content = new CustomContentSection { Stratagems = [s], Images = [Image("drop")] };
        var list = Target.Validate(content, Project(content), null);
        Assert.DoesNotContain(list, d => d.Severity == CustomSeverity.Error);
        Assert.Contains(list, d => d.Severity == CustomSeverity.Warning && d.Field == "payload.items[0].item");
        Assert.Contains("allow_unverified_effect=true", Target.Registration(s));
        Assert.Contains("item=hd2.weapon('AR-23 Liberator')", Target.Registration(s));
    }

    [Fact] public void Runtime_problems_land_on_the_field_they_name()
    {
        var s = CustomTemplates.Stratagems.First(t => t.Family == DeliveryFamily.Pelican).Create();
        s.Icon = "icon";
        s.Delivery.Pelican!.Gun = new() { Rpm = 600 };
        s.Cooldown = 0;
        var content = new CustomContentSection { Stratagems = [s], Images = [Image("icon")] };
        var errors = Target.Validate(content, Project(content), null).Where(d => d.Severity == CustomSeverity.Error).ToArray();
        Assert.Contains(errors, d => d.Runtime && d.Field == "payload.gun.rpm" && d.Area == CustomDiagnostic.Delivery && d.About("payload.gun"));
        Assert.Contains(errors, d => d.Runtime && d.Field == "cooldown" && d.Area == CustomDiagnostic.CallIn);
        Assert.True(errors.Single(d => d.Field == "payload.gun.rpm").About("payload.gun.rpm"));
    }

    // b3f42ec: every support weapon a variant host. Only deliverable hosts are offered; a shared host's variant is written with allow_shared = true
    // (a warning, never a blocker); a host that fires no projectile has no round; an expendable clone takes the reviewed direct damage.
    [Fact] public void Variant_hosts_shared_types_and_direct_damage_build_as_the_Runtime_requires()
    {
        Assert.Equal(32, Target.Describe("payload.weapon", new CustomStratagem { Delivery = new() { Family = DeliveryFamily.Variant, Variant = new() } }).Choices!.Count);
        Assert.DoesNotContain("TX-41 Sterilizer", Target.Describe("payload.weapon", new CustomStratagem { Delivery = new() { Family = DeliveryFamily.Variant, Variant = new() } }).Choices!);
        var s = new CustomStratagem
        {
            Id = "laser_cannon_mk2", Name = "LASER CANNON MK2", Description = "A renamed LAS-98.", Icon = "laser_cannon_mk2", Code = ["down", "up", "left", "right", "down", "down"],
            Carrier = new() { Group = "weapon" }, Delivery = new() { Family = DeliveryFamily.Variant, Variant = new() { Weapon = "LAS-98 Laser Cannon" } },
        };
        var content = new CustomContentSection { Stratagems = [s], Images = [Image(s.Id)] };
        var list = Target.Validate(content, Project(content), null);
        Assert.DoesNotContain(list, d => d.Severity == CustomSeverity.Error);
        Assert.Contains(list, d => d.Severity == CustomSeverity.Warning && d.Field == "payload.weapon" && d.Message.Contains("world loot", StringComparison.Ordinal));
        Assert.Contains("allow_shared=true", Target.Registration(s));
        Assert.Equal(CustomFieldStatus.Unavailable, Target.Describe("payload.round", s).Status);
        // A host that is not shared writes no opt-in.
        s.Delivery.Variant!.Weapon = "GR-8 Recoilless Rifle";
        Assert.DoesNotContain("allow_shared", Target.Registration(s));
        Assert.DoesNotContain(Target.Validate(content, Project(content), null), d => d.Severity is CustomSeverity.Error or CustomSeverity.Warning && d.Field == "payload.weapon");
        // EAT-40 Expendable Gas: the EAT-17's round takes the reviewed 500 with its impact explosion, never with another round.
        var eat = CustomTemplates.Stratagems.Single(t => t.Key == "EAT17GExample").Create();
        Assert.Equal(["500"], Target.Describe("payload.modify.direct_damage", eat).Choices);
        eat.Delivery.Expendable!.Modify!.DirectDamage = 500;
        Assert.Contains("direct_damage=500", Target.Registration(eat));
        eat.Icon = eat.Id;
        var eatContent = new CustomContentSection { Stratagems = [eat], Images = [Image(eat.Id)] };
        Assert.DoesNotContain(Target.Validate(eatContent, Project(eatContent), null), d => d.Severity == CustomSeverity.Error);
        Assert.Equal(CustomFieldStatus.Unavailable, Target.Describe("payload.round", eat).Status);
        eat.Delivery.Expendable.Modify.ImpactPayload = null;
        Assert.Equal(CustomFieldStatus.Unavailable, Target.Describe("payload.modify.direct_damage", eat).Status);
    }

    private static CustomImage Image(string id)
    {
        var png = CustomIconCompiler.Placeholder(id);
        return new() { Id = id, Png = Convert.ToBase64String(png), Sha256 = CustomContentValidator.Sha256(png) };
    }
    private static ModProject Project(CustomContentSection content) => new()
    {
        FormatVersion = 14, DisplayName = "Custom", Author = "Tests", ResourceId = "mods/tests/custom", ManagerGuid = Core.Projects.ProjectIdentity.ManagerGuid("mods/tests/custom"),
        SdkVersion = "0.28.1", CustomContent = content,
    };
}
