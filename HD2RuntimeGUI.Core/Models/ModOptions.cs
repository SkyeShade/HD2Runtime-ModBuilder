namespace HD2RuntimeGUI.Core.Models;

// In-game options (HD2Runtime 0.25.0+ hd2.options, shown by CowboyBingus Mod Options Menu). One page per project; a master
// toggle controls every option-bound operation; each row binds one edited field's value. Rows are registered in list order.
public sealed class ModOptionsSettings
{
    public bool Enabled { get; set; }
    public string PageId { get; set; } = "";
    public string Title { get; set; } = "";
    public string MasterLabel { get; set; } = "Enabled";
    public string? MasterDescription { get; set; }
    // 0.25.1+: behavior without Mod Options Menu. "default" (Runtime's default, recommended for generated mods) applies each
    // option's declared default; "disable" keeps option-bound operations inactive. 0.25.0 has no fallback and always keeps them inactive.
    public string Fallback { get; set; } = "default";
    public List<ModOptionRow> Rows { get; set; } = [];
}

public sealed class ModOptionRow
{
    // Binding key of the edited field (weapon:, object:, entity:, support: or stratagem:); see Generation.ModOptionsService.
    public string Key { get; set; } = "";
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "slider"; // "slider" or "choice"
    public string Label { get; set; } = "";
    public string? Description { get; set; }
    public bool Gap { get; set; }
    // Slider.
    public double Min { get; set; }
    public double Max { get; set; }
    public double Step { get; set; } = 1;
    public double Default { get; set; }
    // Choice: display names, the field value each selects, and the 1-based default.
    public List<string> Choices { get; set; } = [];
    public List<double> Values { get; set; } = [];
    public int DefaultIndex { get; set; } = 1;
}
