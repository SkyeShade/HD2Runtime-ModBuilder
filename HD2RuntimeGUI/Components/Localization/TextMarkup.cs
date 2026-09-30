using HD2RuntimeGUI.Core.Localization;
using Microsoft.AspNetCore.Components;

namespace HD2RuntimeGUI.Components;

public static class TextMarkup
{
    /// <summary>A ".Html" resource (inline &lt;code&gt;, &lt;strong&gt;, &lt;em&gt;, &lt;br&gt;) rendered as markup; arguments are HTML-encoded.</summary>
    public static MarkupString Markup(this IUiText text, string key, params object?[] args) => new(text.Html(key, args));
}
