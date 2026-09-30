using Xunit;

namespace HD2RuntimeGUI.Tests;

// The per-edit save path (BuilderWorkspace.SaveChangesAsync): an edit that leaves the project as saved writes, regenerates and re-lists
// nothing; a real edit saves, regenerates, and updates the library entry in memory exactly as the store wrote it.
public sealed class SavePathTests
{
    [Fact] public async Task An_edit_that_changes_nothing_is_not_saved_again_and_a_real_edit_is()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var f = sdk.Stratagems!.FieldInstances.First(x => x.Editable && LargeProjectFixture.Next(x.CurrentDefault, x.Type, x.Min, x.Max) != null);
        var value = LargeProjectFixture.Next(f.CurrentDefault, f.Type, f.Min, f.Max)!;
        await w.SetStratagemAsync(f.InstanceKey, value);
        var file = e.Paths.ProjectFile(w.Project!.Id); var written = File.GetLastWriteTimeUtc(file); var modified = w.Project.ModifiedAt; var lua = w.LuaPreview;
        Assert.Contains(f.ApiFieldConstant, lua);
        await Task.Delay(50);
        // The same value committed again: nothing to save or rebuild.
        await w.SetStratagemAsync(f.InstanceKey, value);
        Assert.Equal(written, File.GetLastWriteTimeUtc(file)); Assert.Equal(modified, w.Project.ModifiedAt); Assert.Same(lua, w.LuaPreview);
        Assert.Single(w.Project.StratagemChanges);
        // Back to the baseline removes the edit: saved and regenerated.
        await w.SetStratagemAsync(f.InstanceKey, f.CurrentDefault.GetRawText());
        Assert.Empty(w.Project.StratagemChanges); Assert.True(w.Project.ModifiedAt > modified); Assert.NotEqual(written, File.GetLastWriteTimeUtc(file));
        Assert.DoesNotContain(f.ApiFieldConstant, w.LuaPreview);
        // The library in memory is what the store holds, without reading it back after the edit.
        Assert.Equal(await e.Store.ListAsync(), w.Library);
        // Reopened from disk: the saved state.
        var saved = w.LuaPreview; await w.OpenAsync(w.Project.Id);
        Assert.Empty(w.Project!.StratagemChanges); Assert.Equal(saved, w.LuaPreview);
    }
}
