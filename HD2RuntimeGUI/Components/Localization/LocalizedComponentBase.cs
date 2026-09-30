using Microsoft.AspNetCore.Components;

namespace HD2RuntimeGUI.Components;

// Every component inherits this base (set in _Imports.razor). Routes cascades the UI language id; when the language changes the
// cascade notifies each component, which re-renders in the new language with its state (drafts, selections, open panels) intact.
public abstract class LocalizedComponentBase : ComponentBase
{
    public const string LanguageCascade = "UiLanguage";
    [CascadingParameter(Name = LanguageCascade)] public string? UiLanguage { get; set; }
}
