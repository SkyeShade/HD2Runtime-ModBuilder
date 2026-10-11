using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.CustomContent;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Models;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// The custom content engine (1.7.0): the Runtime schema reader, the 0.30.0 target adapter (field descriptions, Runtime rules, the
// generated Lua), ids, the icon compiler and resource archive (byte for byte against HD2Runtime's own SDK tools) and the section's migrations.
// The validator and compiler ports are held to the Runtime SDK's own output in CustomStratagemConformanceTests.
public sealed class CustomContentEngineTests
{
    private static readonly Runtime030Target Target = (Runtime030Target)CustomContentTargets.Current;
    private static string Fixture(params string[] parts) => Path.Combine([AppContext.BaseDirectory, "Fixtures", "custom-content", .. parts]);
    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    private static CustomContentSection Content(params CustomStratagem[] stratagems) => new() { Stratagems = [.. stratagems] };
    private static ModProject Project(CustomContentSection content) => new()
    {
        FormatVersion = 14, DisplayName = "Custom", Author = "Tests", ResourceId = "mods/tests/custom", ManagerGuid = Core.Projects.ProjectIdentity.ManagerGuid("mods/tests/custom"),
        SdkVersion = "0.28.1", CustomContent = content,
    };
    // A bundled template by its file name (CustomContent/Templates/<name>.json).
    private static CustomStratagem Template(string key) => CustomTemplates.Stratagems.Single(t => t.Key == key).Create();
    // Any HD2Runtime example (sdk/fixtures/custom_stratagems/<name>.json) read into the model, from the conformance oracle that records them.
    private static readonly JsonObject Oracle = (JsonObject)JsonNode.Parse(File.ReadAllText(Fixture("oracle", "runtime-oracle.json")))!;
    private static CustomStratagem Example(string name) => RuntimeProjectForm.Read((JsonObject)((JsonArray)Oracle["examples"]!)
        .Single(e => e!["name"]!.GetValue<string>() == name)!["project"]!["stratagems"]![0]!.DeepClone());
    private static IReadOnlyList<CustomDiagnostic> Errors(CustomStratagem s, CustomContentSection? content = null)
    {
        content ??= Content(s);
        return [.. Target.Validate(content, Project(content), null).Where(d => d.Severity == CustomSeverity.Error && d.Owner == CustomDiagnostic.StratagemOwner(s.Id))];
    }

    // ---- schema ---------------------------------------------------------------------------------------------------------------------------

    [Fact] public void The_bundled_schema_is_HD2Runtime_0_30_dev_and_reads_every_family_refusal_and_catalogue()
    {
        var schema = Target.Schema;
        // HD2Runtime 0.31.0's schema (0.30.2 changed its sound catalogue since 0.30.0, 0.30.3 its uses range); the custom stratagem API it
        // describes is 0.30.0's.
        Assert.Equal("0.31.0", schema.RuntimeVersion); Assert.Equal(RuntimeRequirement.Release030, Target.MinimumRuntime); Assert.Equal("hd2runtime-custom-stratagems/1", schema.Format); Assert.Equal(1, schema.SchemaVersion);
        Assert.Equal("hd2.custom_stratagem.register", schema.Api); Assert.Equal("bundled", schema.Source);
        Assert.Equal(["script", "support", "expendable", "sentry", "eagle", "orbital", "pelican", "silo", "pod", "weapon"], schema.Families.Select(f => f.Family));
        Assert.Equal(["support", "expendable", "sentry", "eagle", "orbital", "pelican", "silo", "pod", "weapon"], schema.Families.Where(f => f.Builder).Select(f => f.Family));
        Assert.Equal(["support_pod"], schema.Family("pod")!.Groups); Assert.Equal(["weapon"], schema.Family("weapon")!.Groups); Assert.Equal(["expendable"], schema.Family("expendable")!.Groups);
        Assert.Equal(16, schema.MaxDefinitions); Assert.Equal(64, schema.MaxOrbitalShells);
        Assert.Contains(schema.Refused, r => r.Field == "orbital.explosion_scale" && r.Reason.Contains("shared row"));
        Assert.Equal(["Orbital EMS Strike", "Orbital Gas Strike"], schema.Catalogs.Explosions.Select(e => e.Name));
        Assert.Equal(46, schema.Catalogs.SupportDonors.Count); Assert.Equal(22, schema.Catalogs.Rounds.Count); Assert.Equal(10, schema.Catalogs.Orbitals.Count);
        Assert.Equal(new OrbitalPattern(5, 3, 0.75, 2, 27, 0), schema.Orbital("Orbital 120mm HE Barrage")!.Pattern);
        var eat = schema.SupportDonor("EAT-17 Expendable Anti-Tank")!;
        Assert.True(eat.Deliverable && eat.Modifiable && eat.FiresProjectiles); Assert.Equal(2, eat.Count);
        Assert.False(schema.SupportDonor("TX-41 Sterilizer")!.Deliverable);
        Assert.True(schema.SupportDonor("MG-43 Machine Gun")!.RateSelector);
        // The catalogues of 0.30's families: carrier groups, pod items and racks, clones, variants, sounds, silos, blasts and native codes.
        Assert.Equal(["orbital", "any_red", "any", "support", "support_pod", "expendable", "weapon", "sentry", "emplacement", "eagle"], schema.Catalogs.CarrierGroups.Select(g => g.Name));
        Assert.Contains("pelican", schema.Group("any_red")!.Defaults);
        Assert.Equal(104, schema.Catalogs.PodItems.Count); Assert.True(schema.PodItem("player_weapon/AR-23 Liberator")!.Unverified);
        Assert.Equal(["RL-77 Airburst Rocket Launcher"], schema.CloneDonor("EAT-17 Expendable Anti-Tank")!.Rounds);
        Assert.Contains(schema.VariantDonor("M-1000 Maxigun")!.Rounds, r => r.Output == "output/v1/projectile/las-58-talon");
        Assert.Equal(171, schema.Catalogs.Sounds.Count); Assert.Equal(149, schema.Catalogs.NativeCodes.Count);
        Assert.Equal(("slow", 60d), (schema.PelicanExplosion("EMS mortar field")!.Kind, schema.PelicanExplosion("EMS mortar field")!.MaxRpm!.Value));
        Assert.True(schema.Blast("Cyborg Production Unit")!.Objective);
        Assert.Equal("live", schema.EvidenceOf("expendable")!.Status); Assert.Equal("offline", schema.EvidenceOf("pod")!.Status);
        // Ranges are the Runtime's own; enums keep their typed values (a Pelican's rate multiplier is a number, its face_target a boolean).
        var salvos = schema.Field("orbital.salvos")!; Assert.Equal((1d, 16d), (salvos.Min, salvos.Max));
        var spread = schema.Field("support.items[].modify.spread")!; Assert.True(spread.ExclusiveMin); Assert.Equal("milliradians (full width)", spread.Unit);
        Assert.Equal([1d, 1.5, 2d], schema.Field("pelican.gun.rate_multiplier")!.RawValues.Cast<double>());
        Assert.Equal([true], schema.Field("pelican.gun.face_target")!.RawValues.Cast<bool>());
        Assert.Equal((1d, 8d), (schema.Field("expendable.pod[].count")!.Min, schema.Field("expendable.pod[].count")!.Max));
        Assert.Equal(["up", "down", "left", "right"], schema.Field("metadata.code")!.Values);
    }

    [Fact] public void Describe_reads_each_field_by_its_project_form_path_and_explains_refusals_instead_of_hiding_them()
    {
        var info = Target.Describe("payload.shell_interval", new CustomStratagem { Delivery = CustomFields.NewDelivery(DeliveryFamily.Orbital) });
        Assert.True(info.Supported); Assert.Equal("seconds", info.Unit); Assert.Equal((0d, 10d), (info.Min, info.Max));
        Assert.Equal(CustomFieldStatus.Refused, Target.Describe("orbital.explosion_scale").Status);
        Assert.Contains("shared row", Target.Describe("orbital.explosion_scale").Reason);
        Assert.Contains("EagleComponentData", Target.Describe("eagle.pattern").Reason);
        Assert.Equal(CustomFieldStatus.Refused, Target.Describe("sentry.weapon.impact_explosion").Status);
        Assert.Equal(CustomFieldStatus.Refused, Target.Describe("payload.items[].modify.projectile#custom").Status);
        // Context: an Eagle has no cooldown or uses; the 500 kg's impact cannot be replaced; a native barrage takes no timings.
        var eagle = Example("EagleStunRocketPodsExample");
        Assert.Equal(CustomFieldStatus.Refused, Target.Describe("cooldown", eagle).Status);
        Assert.Equal(CustomFieldStatus.Refused, Target.Describe("uses", eagle).Status);
        Assert.Equal("3", Target.Describe("payload.uses", eagle).Default);
        eagle.Delivery.Eagle!.Strike = "Eagle 500kg Bomb";
        Assert.Equal(CustomFieldStatus.Refused, Target.Describe("payload.payload.impact_explosion", eagle).Status);
        Assert.Equal("1", Target.Describe("payload.uses", eagle).Default);
        var barrage = Template("GasBarrageExample");
        Assert.Equal(CustomFieldStatus.Unavailable, Target.Describe("payload.salvos", barrage).Status);
        Assert.True(Target.Describe("payload.impact_explosion", barrage) is { Supported: true, Required: true });
        // The family's own rules are about its payload: a vanilla barrage still has the stratagem's cooldown and uses.
        Assert.True(Target.Describe("cooldown", barrage).Supported); Assert.True(Target.Describe("uses", barrage).Supported);
        Assert.True(Target.Describe("cooldown", Example("PelicanCannonExample")).Supported);
        barrage.Delivery.Orbital = new() { Shell = "Orbital Gas Strike", Pattern = "Orbital 380mm HE Barrage" };
        Assert.Equal("1.5", Target.Describe("payload.shell_interval", barrage).Default);
        // A backpack delivers no weapon; a rate-selector weapon refuses a per-call rate.
        var pack = new CustomStratagem { Id = "pack", Delivery = new() { Family = DeliveryFamily.Support, Support = new() { Item = "B-1 Supply Pack" } } };
        Assert.Equal(CustomFieldStatus.Unavailable, Target.Describe("payload.items[].modify.rpm", pack).Status);
        pack.Delivery.Support!.Item = "MG-43 Machine Gun";
        Assert.Equal(CustomFieldStatus.Refused, Target.Describe("payload.items[].modify.rpm", pack).Status);
        Assert.True(Target.Describe("payload.items[].modify.ammo", pack).Supported);
        Assert.Equal((2d, 2d), (Target.Describe("payload.items[].count", Example("GasEatExample")).Min, Target.Describe("payload.items[].count", Example("GasEatExample")).Max));
        // A Pelican's Gatling-only fields wait for the Gatling AI; its sounds and explosions come from their own catalogues.
        var pelican = Example("PelicanCannonExample");
        Assert.Equal(CustomFieldStatus.Unavailable, Target.Describe("payload.gun.rpm", pelican).Status);
        pelican.Delivery.Pelican!.Gun = new() { BehaveAs = "gatling_sentry" };
        Assert.True(Target.Describe("payload.gun.rpm", pelican).Supported);
        Assert.Equal(171, Target.Describe("payload.gun.sound", pelican).Choices!.Count);
        Assert.Contains("EMS mortar field", Target.Describe("payload.gun.impact_explosion", pelican).Choices!);
        // Choices follow the donor: a clone's rounds, a variant's outputs (shown by their label), the groups that can carry the payload.
        Assert.Equal(["RL-77 Airburst Rocket Launcher"], Target.Describe("payload.round", Example("EAT23Example")).Choices);
        var variant = new CustomStratagem { Delivery = new() { Family = DeliveryFamily.Variant, Variant = new() { Weapon = "M-1000 Maxigun" } } };
        Assert.Contains("output/v1/projectile/las-58-talon", Target.Describe("payload.round", variant).Choices!);
        Assert.Equal("LAS-58 Talon", Target.ChoiceLabel("payload.round", "output/v1/projectile/las-58-talon"));
        Assert.Equal(["weapon"], Target.CarrierGroups(variant).Select(g => g.Name));
        Assert.Equal(["orbital", "any_red", "any", "support"], Target.CarrierGroups(pelican).Select(g => g.Name));
        Assert.Equal("any_red", Target.DefaultCarrierGroup(pelican));
        Assert.Equal("AR-23 Liberator", Target.ChoiceLabel("payload.items[].item", "player_weapon/AR-23 Liberator"));
    }

    [Fact] public void A_schema_with_another_format_or_missing_fields_is_handled_by_the_adapter_not_the_editors()
    {
        var json = JsonNode.Parse(CustomContentTargets.BundledSchema())!.AsObject();
        json["families"]!.AsArray().Select(f => f!.AsObject()).Single(f => (string)f["family"]! == "orbital")["fields"]!.AsObject().Remove("salvo_scatter");
        var target = (Runtime030Target)CustomContentTargets.FromSchema(JsonSerializer.SerializeToUtf8Bytes(json), "test");
        var orbital = new CustomStratagem { Delivery = CustomFields.NewDelivery(DeliveryFamily.Orbital) };
        Assert.Equal(CustomFieldStatus.Unavailable, target.Describe("payload.salvo_scatter", orbital).Status);
        Assert.True(target.Describe("payload.scatter", orbital).Supported);
        Assert.Throws<InvalidDataException>(() => CustomSchemaReader.Read("{}"u8.ToArray(), "broken"));
    }

    // ---- ids ----------------------------------------------------------------------------------------------------------------------------

    [Fact] public void Ids_are_suggested_once_from_names_validated_and_made_unique()
    {
        Assert.Equal("orbital_gas_barrage", CustomIds.Suggest("Orbital Gas Barrage", []));
        Assert.Equal("eat17g_gas_expendable", CustomIds.Suggest("EAT-17G Gas Expendable Anti-Tank", []));
        Assert.Equal("orbital_gas_barrage_2", CustomIds.Suggest("Orbital Gas Barrage", ["orbital_gas_barrage"]));
        Assert.Equal("custom_380mm_gas", CustomIds.Suggest("380mm Gas", []));
        Assert.Null(CustomIds.Suggest("—", []));
        Assert.True(CustomIds.Valid("eat17_gas")); Assert.False(CustomIds.Valid("17eat")); Assert.False(CustomIds.Valid("Eat")); Assert.False(CustomIds.Valid(new string('a', 49)));
        Assert.Equal(48, CustomIds.Unique(new string('a', 48), [new string('a', 48)]).Length);
        Assert.Equal("tests/custom/gas_rocket", CustomIds.RuntimeProjectileId("mods/tests/custom", "gas_rocket"));
        var longResource = "mods/" + new string('a', 60) + "/" + new string('b', 60);
        Assert.StartsWith("mb_", CustomIds.RuntimeProjectileId(longResource, "gas_rocket"));
        Assert.True(CustomIds.RuntimeProjectileId(longResource, "gas_rocket").Length <= 96);
    }

    [Fact] public void A_unique_call_in_code_never_equals_starts_or_is_started_by_another()
    {
        IReadOnlyList<string>[] taken = [["down", "down", "right", "up", "right"], ["up", "up"]];
        Assert.Equal(["left", "right"], CustomIds.UniqueCode(["left", "right"], taken));
        Assert.Equal(["down", "down", "right", "up", "up"], CustomIds.UniqueCode(taken[0], taken));
        var fromPrefix = CustomIds.UniqueCode(["up", "up", "down"], taken)!;
        Assert.All(taken, t => Assert.False(fromPrefix.Take(t.Count).SequenceEqual(t.Take(fromPrefix.Count))));
        // Every one-input code taken: the first free two-input code is impossible too (each starts with a taken one).
        Assert.Null(CustomIds.UniqueCode(["up"], [["up"], ["down"], ["left"], ["right"]], max: 3));
        // "down" would start "down down": the next free last input is left.
        Assert.Equal(["left"], CustomIds.UniqueCode(["up"], [["up"], ["down", "down"]], max: 3));
        // A refused code (a native one) is never suggested.
        Assert.Equal(["right"], CustomIds.UniqueCode(["up"], [], c => c[0] is "up" or "down" or "left", max: 3));
    }

    // ---- the adapter's Runtime rules ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("eagle cooldown")]
    [InlineData("eagle uses")]
    [InlineData("eagle impact on the 500kg")]
    [InlineData("support count")]
    [InlineData("backpack weapon change")]
    [InlineData("rate selector rpm")]
    [InlineData("sentry impact payload")]
    [InlineData("native barrage timings")]
    [InlineData("native barrage without payload")]
    [InlineData("too many shells")]
    [InlineData("custom round in a stratagem")]
    [InlineData("fire rate out of range")]
    [InlineData("magazine not whole")]
    [InlineData("spread at its exclusive minimum")]
    [InlineData("beacon of a sentry")]
    [InlineData("carrier families of an eagle")]
    [InlineData("group that cannot carry the payload")]
    [InlineData("scripted without on_activate")]
    [InlineData("on_delivered without a delivery")]
    [InlineData("callback syntax")]
    [InlineData("extra fields syntax")]
    [InlineData("unknown donor")]
    [InlineData("code too long")]
    [InlineData("native code")]
    [InlineData("pelican without hover")]
    [InlineData("pelican rpm without the Gatling AI")]
    [InlineData("silo without blast")]
    [InlineData("expendable round with impact")]
    [InlineData("pod without items")]
    [InlineData("variant model missing")]
    [InlineData("presentation icon missing")]
    [InlineData("traits too long")]
    [InlineData("uses out of range")]
    public void What_the_Runtime_refuses_is_refused_before_the_build(string rule)
    {
        var s = rule switch
        {
            "eagle cooldown" => Example("EagleStunRocketPodsExample").With(x => x.Cooldown = 30),
            "eagle uses" => Example("EagleStunRocketPodsExample").With(x => x.Uses = 3),
            "eagle impact on the 500kg" => Example("EagleStunRocketPodsExample").With(x => x.Delivery.Eagle!.Strike = "Eagle 500kg Bomb"),
            "support count" => Example("GasEatExample").With(x => x.Delivery.Support!.Count = 1),
            "backpack weapon change" => Example("GasEatExample").With(x => x.Delivery.Support!.Item = "B-1 Supply Pack"),
            "rate selector rpm" => Example("GasEatExample").With(x => x.Delivery.Support = new() { Item = "MG-43 Machine Gun", Weapon = new() { FireRate = 900 } }),
            "sentry impact payload" => Example("HmgSentryExample").With(x => x.Delivery.Sentry!.Weapon!.ImpactPayload = new(ContentRef.VanillaExplosion, "Orbital Gas Strike")),
            "native barrage timings" => Template("GasBarrageExample").With(x => x.Delivery.Orbital!.Salvos = 3),
            "native barrage without payload" => Template("GasBarrageExample").With(x => x.Delivery.Orbital!.ImpactPayload = null),
            "too many shells" => Template("GasBarrageExample").With(x => x.Delivery.Orbital = new() { Shell = "Orbital Gas Strike", Salvos = 16, ShellsPerSalvo = 5 }),
            "custom round in a stratagem" => Example("HmgSentryExample").With(x => x.Delivery.Sentry!.Weapon!.Projectile = ContentRef.Custom("gas_rocket")),
            "fire rate out of range" => Example("HmgSentryExample").With(x => x.Delivery.Sentry!.Weapon!.FireRate = 10),
            "magazine not whole" => Example("HmgSentryExample").With(x => x.Delivery.Sentry!.Weapon!.Magazine = 0),
            "spread at its exclusive minimum" => Example("HmgSentryExample").With(x => x.Delivery.Sentry!.Weapon!.Spread = 0),
            "beacon of a sentry" => Example("HmgSentryExample").With(x => x.Carrier.Beacon = "offensive"),
            "carrier families of an eagle" => Example("EagleStunRocketPodsExample").With(x => x.Carrier.Allow = ["eagle", "orbital"]),
            "group that cannot carry the payload" => Example("EAT23Example").With(x => x.Carrier.Group = "orbital"),
            "scripted without on_activate" => new CustomStratagem { Id = "scripted", Name = "S", Description = "x", Code = ["up", "right", "up", "right", "left", "left"],
                Carrier = new() { Beacon = "any" }, Delivery = new() { Family = DeliveryFamily.Scripted } },
            "on_delivered without a delivery" => Example("PelicanCannonExample").With(x => x.Callbacks = new() { OnDelivered = "ctx:log('x')" }),
            "callback syntax" => Example("GasEatExample").With(x => x.Callbacks = new() { OnDelivered = "if then end" }),
            "extra fields syntax" => Example("GasEatExample").With(x => x.Callbacks = new() { ExtraFields = "selection = = 'token'" }),
            "unknown donor" => Example("GasEatExample").With(x => x.Delivery.Support!.Item = "EAT-99 Imaginary"),
            "code too long" => Example("GasEatExample").With(x => x.Code = [.. Enumerable.Repeat("up", 9)]),
            "native code" => Example("GasEatExample").With(x => x.Code = ["down", "left", "up", "right"]),
            "pelican without hover" => Example("PelicanCannonExample").With(x => x.Delivery.Pelican!.Hover = null),
            "pelican rpm without the Gatling AI" => Example("PelicanCannonExample").With(x => x.Delivery.Pelican!.Gun = new() { Rpm = 600 }),
            "silo without blast" => Example("ShredderSiloExample").With(x => x.Delivery.Silo!.Blast = null),
            "expendable round with impact" => Example("EAT17CExample").With(x => x.Delivery.Expendable!.Modify = new() { ImpactPayload = new(ContentRef.VanillaExplosion, "Orbital Gas Strike") }),
            "pod without items" => Example("GasEatExample").With(x => { x.Delivery = new() { Family = DeliveryFamily.Pod, Pod = new() }; x.Carrier = new() { Group = "support_pod" }; }),
            "variant model missing" => Example("GasEatExample").With(x => { x.Delivery = new() { Family = DeliveryFamily.Variant, Variant = new() { Weapon = "M-1000 Maxigun", Model = "nope" } }; x.Carrier = new() { Group = "weapon" }; }),
            "presentation icon missing" => Example("EAT23Example").With(x => x.Delivery.Expendable!.Presentation = new() { IconMode = DeliveredPresentation.Custom, Icon = "missing_icon" }),
            "traits too long" => Example("GasEatExample").With(x => x.Traits = [new string('T', 33)]),
            "uses out of range" => Example("GasEatExample").With(x => x.Uses = 101),
            _ => throw new ArgumentException(rule),
        };
        s.Icon = s.Id;
        Assert.NotEmpty(Errors(s));
    }

    // HD2Runtime 0.30.3: a custom stratagem's uses may be 0 or -1, both unlimited (0.30.2 refuses uses below 1). ModBuilder writes no uses for
    // unlimited, which every 0.30 Runtime accepts; an explicit 0 or -1 from an imported Runtime project is kept (it round-trips) and raises the
    // export to 0.30.3, until the field's "Use unlimited (no value)" removes it. Below -1 is refused.
    [Fact] public void Explicit_unlimited_uses_are_kept_from_a_runtime_project_and_require_0_30_3()
    {
        var info = Target.Describe("uses", Example("GasEatExample"));
        Assert.Equal((-1d, 100d, "unlimited"), (info.Min, info.Max, info.Default));
        foreach (var value in new[] { 0, -1 })
        {
            var form = (JsonObject)((JsonArray)Oracle["examples"]!).Single(e => e!["name"]!.GetValue<string>() == "GasEatExample")!["project"]!["stratagems"]![0]!.DeepClone();
            form["uses"] = value;
            var s = RuntimeProjectForm.Read(form); s.Icon = s.Id;
            Assert.Equal(value, s.Uses); Assert.Empty(Errors(s));
            Assert.Equal(value, Target.ProjectForm(s)["uses"]!.GetValue<int>());
            Assert.Contains("    uses=" + value + ",\n", Target.Registration(s));
            var project = Project(Content(s));
            Assert.Contains((RuntimeRequirement.Release0303, "custom stratagem " + s.Id + " uses " + value + " (unlimited)"), RuntimeRequirement.Floors(project));
            Assert.Contains(Target.Validate(Content(s), project, null), d => d.Severity == CustomSeverity.Info && d.Field == "uses" && d.Message.Contains("0.30.3"));
            // The fix: no value, unlimited on every 0.30 Runtime, and the floor is gone.
            s.Uses = null;
            Assert.False(Target.ProjectForm(s).ContainsKey("uses")); Assert.DoesNotContain("uses=", Target.Registration(s));
            Assert.DoesNotContain(RuntimeRequirement.Floors(Project(Content(s))), f => f.Floor == RuntimeRequirement.Release0303);
        }
        Assert.NotEmpty(Errors(Example("GasEatExample").With(x => { x.Uses = -2; x.Icon = x.Id; })));
        Assert.Empty(Errors(Example("GasEatExample").With(x => { x.Uses = 100; x.Icon = x.Id; })));
    }

    [Fact] public void Codes_that_equal_or_start_another_custom_stratagems_code_are_refused_on_both()
    {
        var a = Template("GasBarrageExample"); var b = Example("GasEatExample"); b.Code = [.. a.Code.Take(3)];
        var errors = Target.Validate(Content(a, b), Project(Content(a, b)), null).Where(d => d.Severity == CustomSeverity.Error && d.Field == "code" && d.Message.Contains("collides")).ToArray();
        Assert.Single(errors);
        Assert.Equal(CustomDiagnostic.StratagemOwner(b.Id), errors[0].Owner);
        b.Code = ["down", "up", "down", "down", "left", "right", "down"];
        Assert.DoesNotContain(Target.Validate(Content(a, b), Project(Content(a, b)), null), d => d.Severity == CustomSeverity.Error && d.Field == "code");
        Assert.Contains(Target.Validate(Content(a, b), Project(Content(a, b)), null), d => d.Severity == CustomSeverity.Pass && d.Field == "code" && d.Owner == CustomDiagnostic.StratagemOwner(b.Id));
    }

    [Fact] public void Multiplayer_hints_favour_declarative_payloads_and_flag_callbacks()
    {
        var gas = Example("GasEatExample"); gas.Icon = gas.Id;
        var hints = Target.Validate(Content(gas), Project(Content(gas)), null).Where(d => d.Area == CustomDiagnostic.Multiplayer).ToArray();
        Assert.Contains(hints, d => d.Severity == CustomSeverity.Pass && d.Message.Contains("declarative"));
        Assert.DoesNotContain(hints, d => d.Severity is CustomSeverity.Warning or CustomSeverity.Error);
        // Runtime's own live status of the family is shown, not guessed.
        Assert.Contains(hints, d => d.Severity == CustomSeverity.Info && d.Message.Contains("live"));
        gas.Callbacks = new() { OnDelivered = "for _, weapon in ipairs(ctx.weapons) do weapon:set_impact_explosion('Orbital EMS Strike') end" };
        hints = Target.Validate(Content(gas), Project(Content(gas)), null).Where(d => d.Area == CustomDiagnostic.Multiplayer).ToArray();
        Assert.Contains(hints, d => d.Severity == CustomSeverity.Warning && d.Message.Contains("caller"));
        Assert.Empty(Errors(gas));
    }

    // ---- generated Lua ------------------------------------------------------------------------------------------------------------------

    [Fact] public void Generated_Lua_is_the_SDKs_registration_deterministic_and_independent_of_the_culture()
    {
        var barrage = Template("GasBarrageExample"); barrage.Icon = barrage.Id;
        var runtime = new CustomStratagem
        {
            Id = "runtime_barrage", Name = "RUNTIME BARRAGE", Description = "A Runtime barrage.", Icon = "runtime_barrage", Code = ["left", "right", "left", "right", "up", "up", "down"], Cooldown = 42.5,
            Carrier = new() { Beacon = "any" }, Delivery = new() { Family = DeliveryFamily.Orbital, Orbital = new() { Shell = "Orbital Gas Strike", Salvos = 3, ShellInterval = 0.25, Scatter = 12.5 } },
        };
        var content = Content(barrage, runtime);
        string Compile() => Target.Compile(content, Project(content), null!, images: false).Section;
        var first = Compile();
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try { System.Globalization.CultureInfo.CurrentCulture = new("de-DE"); Assert.Equal(first, Compile()); }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
        Assert.Contains("""
                    register_custom_stratagem('orbital_gas_barrage', function()
                        return {
                            id='orbital_gas_barrage',
                            name='ORBITAL GAS BARRAGE',
                            name_cased='Orbital Gas Barrage',
                            description='A prolonged caustic barrage, spreading corrosive gas over a large area. Causes confusion and blindness on those affected. Breathing it in is not advised.',
                            icon=hd2.resources.image('orbital_gas_barrage'),
                            code={'right','right','down','left','down','left'},
                            cooldown=60,
                            traits={'Orbital','Anti-Tank','Caustic'},
                            carrier={beacon='offensive',prefer_families={'orbital'}},
        """.Replace("\r\n", "\n"), first);
        Assert.Contains("orbital={native=true,pattern='Orbital 120mm HE Barrage',impact_explosion='Orbital Gas Strike'},", first);
        Assert.Contains("cooldown=42.5,", first); Assert.Contains("shell_interval=0.25", first); Assert.Contains("scatter=12.5", first);
        Assert.DoesNotContain("42,5", first);
        Assert.Contains("pcall(hd2.custom_stratagem.register, definition)", first);
        Assert.Contains("run_as, \"mods/tests/custom\", custom_content", first);
        Assert.Empty(Core.Scripting.LuaScriptAnalyzer.Analyze("local hd2={}\n" + first, null).Where(d => d.Severity == Core.Scripting.LuaDiagnostic.Error));
    }

    // ---- icons and the archive, byte for byte against HD2Runtime's SDK tools ------------------------------------------------------------

    [Fact] public void Png_decoding_mask_preparation_bc1_and_texture_headers_match_the_SDK_image_build()
    {
        var reference = JsonNode.Parse(File.ReadAllText(Fixture("image-reference.json")))!;
        foreach (var (name, item) in reference["images"]!.AsObject())
        {
            var png = File.ReadAllBytes(Fixture(name + ".png"));
            var image = CustomIconCompiler.Read(png);
            Assert.Equal(((int)item!["width"]!, (int)item["height"]!), (image.Width, image.Height));
            Assert.Equal((string)item["rgba"]!, Sha(image.Rgba));
            foreach (var mode in new[] { "auto", "raw" })
            {
                if (item[mode] is not { } expected) continue;
                var (prepared, label) = CustomIconCompiler.Prepare(png, mode);
                Assert.Equal((string)expected["label"]!, label); Assert.Equal((string)expected["prepared"]!, Sha(prepared));
                var (main, gpu, _) = CustomIconCompiler.Texture(png, mode);
                Assert.Equal((string)expected["main"]!, Sha(main)); Assert.Equal((string)expected["gpu"]!, Sha(gpu)); Assert.Equal((int)expected["gpuLength"]!, gpu.Length);
            }
        }
        var material = reference["material"]!;
        Assert.Equal(ulong.Parse((string)material["hash"]!), GameplayArchive.ResourceHash((string)material["name"]!));
        Assert.Equal((string)material["sha256"]!, Sha(CustomIconCompiler.Material(ulong.Parse((string)material["hash"]!))));
    }

    [Fact] public void The_resource_archive_matches_the_SDK_layout_byte_for_byte()
    {
        var r = JsonNode.Parse(File.ReadAllText(Fixture("image-reference.json")))!["archive"]!;
        var resource = (string)r["resource"]!;
        var resources = new Dictionary<(ulong Type, ulong Name), (byte[] Main, byte[] Gpu)>
        {
            [(ResourceArchive.LuaType, GameplayArchive.ResourceHash(resource))] = (ResourceArchive.LuaResource(System.Text.Encoding.UTF8.GetBytes((string)r["lua"]!)), []),
            [(ResourceArchive.LuaType, GameplayArchive.ResourceHash(resource + "/hd2runtime_images"))] = (ResourceArchive.LuaResource(System.Text.Encoding.UTF8.GetBytes((string)r["record"]!)), []),
        };
        foreach (var image in r["images"]!.AsArray().Select(i => (string)i!))
        {
            var key = GameplayArchive.ResourceHash(resource + "/images/" + image);
            var (main, gpu, _) = CustomIconCompiler.Texture(File.ReadAllBytes(Fixture(image + ".png")), "auto");
            resources[(CustomIconCompiler.TextureType, key)] = (main, gpu);
            resources[(CustomIconCompiler.MaterialType, key)] = (CustomIconCompiler.Material(key), []);
        }
        var (archive, gpuResources) = ResourceArchive.Build(resources);
        Assert.Equal((int)r["length"]!, archive.Length); Assert.Equal((int)r["gpuLength"]!, gpuResources.Length);
        Assert.Equal((string)r["sha256"]!, Sha(archive)); Assert.Equal((string)r["gpuSha256"]!, Sha(gpuResources));
    }

    [Fact] public void Icons_are_validated_and_placeholders_and_previews_are_valid_icons()
    {
        Assert.Null(CustomIconCompiler.IconIssue(File.ReadAllBytes(Fixture("mask.png"))));
        Assert.Contains("256", CustomIconCompiler.IconIssue(File.ReadAllBytes(Fixture("grey2.png"))));
        Assert.NotNull(CustomIconCompiler.IconIssue("not a png"u8.ToArray()));
        // An interlaced PNG is refused with its reason (IHDR byte 12, the CRC recomputed).
        var png = File.ReadAllBytes(Fixture("grey2.png")); png[8 + 8 + 12] = 1;
        var crc = Crc(png.AsSpan(12, 17)); png[29] = (byte)(crc >> 24); png[30] = (byte)(crc >> 16); png[31] = (byte)(crc >> 8); png[32] = (byte)crc;
        Assert.Throws<InvalidDataException>(() => CustomIconCompiler.Read(png));
        var placeholder = CustomIconCompiler.Placeholder("orbital_gas_barrage");
        Assert.Null(CustomIconCompiler.IconIssue(placeholder));
        Assert.Equal("masks (as given)", CustomIconCompiler.Prepare(placeholder, "auto").Label);
        Assert.Equal(placeholder, CustomIconCompiler.Placeholder("orbital_gas_barrage"));
        Assert.NotEqual(placeholder, CustomIconCompiler.Placeholder("gas_eat"));
        Assert.Null(CustomIconCompiler.IconIssue(CustomIconCompiler.Preview(placeholder, "auto", (255, 128, 120))));
    }
    private static uint Crc(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFU;
        foreach (var b in data) { crc ^= b; for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320U ^ (crc >> 1) : crc >> 1; }
        return crc ^ 0xFFFFFFFFU;
    }

    // ---- migrations ---------------------------------------------------------------------------------------------------------------------

    [Fact] public void The_custom_content_section_is_migrated_step_by_step_and_a_newer_one_is_refused()
    {
        JsonObject Project(int? version) => new() { ["customContent"] = version == null ? new JsonObject() : new JsonObject { ["version"] = version, ["stratagems"] = new JsonArray() } };
        Assert.Equal(0, CustomContentMigrations.Migrate(new JsonObject()));
        Assert.Equal(0, CustomContentMigrations.Migrate(Project(CustomContentSection.CurrentVersion)));
        Assert.Contains("newer", Assert.Throws<InvalidDataException>(() => CustomContentMigrations.Migrate(Project(CustomContentSection.CurrentVersion + 1))).Message);
        Assert.Throws<InvalidDataException>(() => CustomContentMigrations.Migrate(Project(null)));
        Assert.Throws<InvalidDataException>(() => CustomContentMigrations.Migrate(Project(0)));
        // Steps run in order, each from its version to the next, and the section ends at the current version.
        var steps = new CustomContentMigrations.Step[]
        {
            new(1, "rename stratagems to customStratagems", s => { s["customStratagems"] = s["stratagems"]!.DeepClone(); s.Remove("stratagems"); }),
            new(2, "add a marker", s => s["migrated"] = true),
        };
        var project = Project(1);
        Assert.Equal(2, CustomContentMigrations.Migrate(project, steps, 3));
        Assert.Equal(3, (int)project["customContent"]!["version"]!); Assert.True((bool)project["customContent"]!["migrated"]!);
        Assert.NotNull(project["customContent"]!["customStratagems"]);
        Assert.Throws<InvalidDataException>(() => CustomContentMigrations.Migrate(Project(1), [steps[1]], 3));
    }
}

internal static class CustomStratagemTestExtensions
{
    public static CustomStratagem With(this CustomStratagem s, Action<CustomStratagem> edit) { edit(s); return s; }
}
