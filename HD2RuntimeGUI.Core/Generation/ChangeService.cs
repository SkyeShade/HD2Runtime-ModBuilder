using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

public interface IChangeService
{
    ModChange Create(SdkMetadata sdk, string target, string field, string value, bool ensure, string group);
    void Validate(SdkMetadata sdk, ModChange change);
}
public sealed class ChangeService : IChangeService
{
    public ModChange Create(SdkMetadata sdk, string target, string field, string value, bool ensure, string group)
    {
        var f = sdk.Resources[target].Fields[field];
        JsonNode? parsed;
        try { parsed = JsonNode.Parse(value); } catch (System.Text.Json.JsonException e) { throw new InvalidDataException("Enter a valid field value.", e); }
        var change = new ModChange { Target = target, Domain = f.Domain, Field = field, ExpectedValue = f.Expected?.DeepClone(),
            NewValue = parsed, FieldType = f.ValueType, Confidence = f.Confidence, EnsureEnabled = ensure, Group = string.IsNullOrWhiteSpace(group) ? "Gameplay" : group.Trim() };
        Validate(sdk, change); return change;
    }
    public void Validate(SdkMetadata sdk, ModChange change)
    {
        if (!sdk.Resources.TryGetValue(change.Target, out var target) || !target.Fields.TryGetValue(change.Field, out var field)) throw new InvalidDataException("Change is not present in this project's SDK.");
        if (!field.Writable) throw new InvalidDataException("This SDK field is read-only.");
        if (change.Domain != field.Domain || change.FieldType != field.ValueType || change.Id == Guid.Empty || change.Group.Length > 120)
            throw new InvalidDataException("Change metadata is inconsistent.");
        MetadataReader.ValidateValue(field.ValueType, change.NewValue);
        MetadataReader.ValidateValue(field.ValueType, change.ExpectedValue);
        // These are public generation contracts, not a copy of the runtime's memory safety engine.
        if (!sdk.Transitions.Any(t => t.Resource == change.Target && t.Field == change.Field && JsonNode.DeepEquals(t.Expected, change.ExpectedValue) && JsonNode.DeepEquals(t.Value, change.NewValue)))
            throw new InvalidDataException("The SDK has no supported generation contract for this expected/new value pair. Choose the SDK's reviewed value.");
    }
}
