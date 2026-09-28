using HD2RuntimeGUI.Core.Projects;
using Xunit;

namespace HD2RuntimeGUI.Tests;

public sealed class ResourceIdSuggestionTests
{
    [Theory]
    [InlineData("SkyeShade", "My Gameplay Mod", "mods/skyeshade/my_gameplay_mod")]
    [InlineData("  Skye Shade ", "Laser  Sentry++ v2!", "mods/skye_shade/laser_sentry_v2")]
    [InlineData("Zoë", "Crème Brûlée", "mods/zoe/creme_brulee")]
    [InlineData("A-1", "E/AT-12 Buff", "mods/a_1/e_at_12_buff")]
    public void Suggestion_slugs_author_and_mod_name(string author, string name, string expected)
    {
        var id = ProjectIdentity.SuggestResourceId(author, name);
        Assert.Equal(expected, id); ProjectIdentity.ValidateResource(id!);
    }
    [Theory] [InlineData("", "Mod")] [InlineData("Author", "")] [InlineData("日本", "Mod")] [InlineData("Author", "!!!")] [InlineData(null, null)]
    public void No_suggestion_without_usable_author_and_name(string? author, string? name) => Assert.Null(ProjectIdentity.SuggestResourceId(author, name));
    [Fact] public void Suggestion_avoids_library_ids_and_reserved_runtime_id()
    {
        Assert.Equal("mods/skye/mod_3", ProjectIdentity.SuggestResourceId("Skye", "Mod", ["mods/skye/mod", "MODS/SKYE/MOD_2"]));
        Assert.Null(ProjectIdentity.SuggestResourceId("SkyeShade", "HD2Runtime"));
    }
    [Fact] public void Long_names_stay_within_the_discovery_limit()
    {
        var id = ProjectIdentity.SuggestResourceId(new string('a', 300), new string('b', 300))!;
        Assert.Equal("mods/" + new string('a', 48) + "/" + new string('b', 64), id); ProjectIdentity.ValidateResource(id);
    }
}
