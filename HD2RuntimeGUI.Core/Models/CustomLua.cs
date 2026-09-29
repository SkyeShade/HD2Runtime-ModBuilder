namespace HD2RuntimeGUI.Core.Models;

// Format 11: hand-written HD2Runtime Lua kept with the project (event scripting, timers, keybinds, anything the Runtime API offers).
// The project stores exactly the text ModBuilder last saved; src/addon.lua in the project folder is its working copy for external
// editors, compared against it to detect outside changes. ModBuilder never rewrites it on its own.
public sealed record CustomLuaSettings
{
    public const string RelativePath = "src/addon.lua";
    public const int MaxLength = 1_000_000;
    public bool Enabled { get; init; } = true;
    public string Source { get; init; } = "";
}
