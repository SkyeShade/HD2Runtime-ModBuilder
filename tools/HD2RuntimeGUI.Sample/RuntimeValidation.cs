using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

// Offline acceptance check: runs GUI-generated stratagem Lua through HD2Runtime's own public validators
// (patches / transactions / composition_plans) using HD2's standalone Lua 5.1 library, exactly like Runtime's
// release tests. No game process is started or read; the validators only inspect descriptors against the SDK database.
internal static class RuntimeValidation
{
    private const string Harness = """
        package.preload['mods/skyeshade/hd2runtime']=function()
            local api=require('hd2runtime/api/hd2')
            local patches=require('hd2runtime/domains/patches')
            local transactions=require('hd2runtime/domains/transactions')
            local plans=require('hd2runtime/domains/composition_plans')
            local M=setmetatable({},{__index=api})
            function M.patch(r)patches.validate(r)return 'ok' end
            function M.transaction(r)transactions.validate(r)return 'ok' end
            function M.plan(r)plans.validate(r)return 'ok' end
            function M.ensure(r)
                if r.patch then patches.validate(r.patch)
                elseif r.transaction then transactions.validate(r.transaction)
                elseif r.plan then plans.validate(r.plan)
                else error('ensure requires patch, transaction or plan')end
                return 'ok'
            end
            return M
        end

        """;
    public static int Run(SdkMetadata sdk, ILuaGenerator generator, string source, string dll, string samples)
    {
        var prelude = Modules(source) + Harness;
        using var lua = new Lua51(dll);
        var catalog = StratagemChangeService.Catalog(sdk); var service = new StratagemChangeService();
        int passed = 0, failed = 0, rejected = 0, limited = 0;
        void Expect(string label, string program, bool accept)
        {
            var error = lua.Execute(prelude + program);
            if ((error == null) == accept) passed++;
            else { failed++; Console.WriteLine($"FAIL {label}: {(accept ? error : "Runtime accepted a request it must reject")}"); }
        }
        ModProject Project(IEnumerable<StratagemField> fields)
        {
            var p = new ModProject { DisplayName = "Runtime validation", Author = "SkyeShade", ResourceId = "mods/skyeshade/runtime_validation",
                ManagerGuid = HD2RuntimeGUI.Core.Projects.ProjectIdentity.ManagerGuid("mods/skyeshade/runtime_validation"), SdkVersion = sdk.Version, ExportDirectory = Path.GetTempPath() };
            foreach (var f in fields)
            {
                var value = f.Type == "integer" ? (f.CurrentDefault.GetInt64() + 1).ToString() : JsonSerializer.Serialize((float)(f.CurrentDefault.GetDouble() + 0.5));
                p.StratagemChanges.Add(service.Create(sdk, f.InstanceKey, value));
                if (f.AllowSharedRequired) p.StratagemApprovals[f.ScopeKey] = StratagemChangeService.ApprovalEvidence(f);
            }
            return p;
        }
        // Every writable canonical instance, one request each. Shared instances must also be rejected without allow_shared.
        foreach (var f in catalog.FieldInstances.Where(f => f.Editable))
        {
            var program = generator.Generate(Project([f]), sdk);
            Expect(f.InstanceKey, program, true);
            if (f.AllowSharedRequired) { Expect(f.InstanceKey + " without allow_shared", program.Replace("allow_shared=true,", ""), false); rejected++; }
            // A stale baseline must be rejected by Runtime's exact expect guard.
            var expect = "expect=" + StratagemScalar.Text(f, f.CurrentDefault) + ",";
            if (!program.Contains(expect)) { failed++; Console.WriteLine($"FAIL {f.InstanceKey}: expected baseline not emitted"); }
            else { Expect(f.InstanceKey + " stale baseline", program.Replace(expect, expect[..^1] + "1,"), false); rejected++; }
        }
        // Every writable field of each stratagem at once: grouped transactions and multi-object plans.
        foreach (var root in catalog.Stratagems)
        {
            var fields = catalog.FieldInstances.Where(f => f.Editable && f.Target.Stratagem == root.Name && f.Target.Path != "eagle_rearm").ToArray();
            if (fields.Length == 0) continue;
            string program;
            try { program = generator.Generate(Project(fields), sdk); }
            catch (InvalidDataException e) when (e.Message.Contains("plan limits")) { limited++; continue; }
            Expect(root.Name + " (all writable fields)", program, true);
        }
        // Vehicles and backpacks (0.23.0+): every writable field, every published mount replacement, and whole-entity plans.
        if (sdk.Entities is { } entities)
        {
            var entitySvc = new EntityChangeService();
            ModProject EntityProject(IEnumerable<(EntityField Field, string? Replacement)> edits)
            {
                var p = Project([]);
                foreach (var (f, replacement) in edits)
                {
                    var value = f.IsReference ? replacement! : f.Type == "integer" ? (f.CurrentDefault.GetInt64() + 1).ToString() : JsonSerializer.Serialize((float)(f.CurrentDefault.GetDouble() + 0.5));
                    var c = entitySvc.Create(sdk, f.InstanceKey, value);
                    if (f.Acknowledgement != null) c = c with { ReferenceAcknowledgement = EntityChangeService.ReferenceEvidence(f, c.DesiredValue) };
                    if (f.AllowSharedRequired) p.EntityApprovals[f.SharedScopeKey] = EntityChangeService.ApprovalEvidence(f);
                    p.EntityChanges.Add(c);
                }
                return p;
            }
            foreach (var f in entities.AllFields.Where(f => f.Editable))
            {
                foreach (var replacement in f.IsReference ? f.AllowedValues!.Where(v => v != f.CurrentDefault.GetString()).ToArray() : [(string?)null])
                {
                    var program = generator.Generate(EntityProject([(f, replacement)]), sdk);
                    Expect(f.InstanceKey + (replacement == null ? "" : " -> " + replacement), program, true);
                    if (f.IsReference) { Expect(f.InstanceKey + " without allow_unverified_reference", program.Replace("allow_unverified_reference=true,", ""), false); rejected++; }
                    if (f.Acknowledgement == "allow_unverified_effect") { Expect(f.InstanceKey + " without allow_unverified_effect", program.Replace("allow_unverified_effect=true,", ""), false); rejected++; }
                    if (f.AllowSharedRequired) { Expect(f.InstanceKey + " without allow_shared", program.Replace("allow_shared=true,", ""), false); rejected++; }
                }
                if (f.IsReference) continue;
                var program2 = generator.Generate(EntityProject([(f, null)]), sdk);
                var expect = "expect=" + EntityScalar.Text(f, f.CurrentDefault) + ",";
                if (!program2.Contains(expect)) { failed++; Console.WriteLine($"FAIL {f.InstanceKey}: expected baseline not emitted"); }
                else { Expect(f.InstanceKey + " stale baseline", program2.Replace(expect, expect[..^1] + "1,"), false); rejected++; }
            }
            foreach (var group in entities.AllFields.Where(f => f.Editable).GroupBy(f => f.PlanGroup))
            {
                string program;
                try { program = generator.Generate(EntityProject(group.Select(f => (f, f.IsReference ? f.AllowedValues![0] == f.CurrentDefault.GetString() ? f.AllowedValues[1] : f.AllowedValues[0] : null))), sdk); }
                catch (InvalidDataException e) when (e.Message.Contains("plan limits")) { limited++; continue; }
                Expect(group.Key + " (all writable fields)", program, true);
            }
        }
        foreach (var file in Directory.Exists(samples) ? Directory.GetFiles(samples, "*.lua").Order(StringComparer.Ordinal).ToArray() : [])
            Expect(Path.GetFileName(file), File.ReadAllText(file), true);
        Console.WriteLine($"Runtime validation: {passed} passed, {failed} failed ({rejected} unsafe variants (missing allow_shared or stale baseline) correctly rejected; {limited} whole-stratagem edits blocked by published plan limits).");
        return failed == 0 ? 0 : 1;
    }
    private static string Modules(string source)
    {
        var output = new StringBuilder();
        foreach (var folder in new[] { "api", "core", "runtime", "schemas", "domains", "examples", "validation", "primary_mapper" })
            foreach (var path in Directory.GetFiles(Path.Combine(source, folder), "*.lua").Order(StringComparer.Ordinal))
            {
                var name = "hd2runtime/" + folder + "/" + Path.GetFileNameWithoutExtension(path);
                output.Append("package.preload['").Append(name).Append("']=function(...)\n").Append(File.ReadAllText(path)).Append("\nend\n");
            }
        return output.ToString();
    }
    private sealed class Lua51 : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr NewState();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void StateAction(IntPtr state);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LoadBuffer(IntPtr state, byte[] buffer, nuint size, string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PCall(IntPtr state, int args, int results, int handler);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ToLString(IntPtr state, int index, out nuint length);
        private readonly IntPtr library; private readonly NewState newState; private readonly StateAction openLibs, close;
        private readonly LoadBuffer load; private readonly PCall pcall; private readonly ToLString toString;
        public Lua51(string dll)
        {
            library = NativeLibrary.Load(dll);
            T Get<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
            newState = Get<NewState>("luaL_newstate"); openLibs = Get<StateAction>("luaL_openlibs"); close = Get<StateAction>("lua_close");
            load = Get<LoadBuffer>("luaL_loadbuffer"); pcall = Get<PCall>("lua_pcall"); toString = Get<ToLString>("lua_tolstring");
        }
        public string? Execute(string program)
        {
            var state = newState(); if (state == IntPtr.Zero) throw new InvalidOperationException("luaL_newstate failed");
            try
            {
                openLibs(state); var bytes = Encoding.UTF8.GetBytes(program);
                var status = load(state, bytes, (nuint)bytes.Length, "@hd2runtime-gui-validation");
                if (status == 0) status = pcall(state, 0, 1, 0);
                if (status == 0) return null;
                var pointer = toString(state, -1, out var length);
                return pointer == IntPtr.Zero ? "Lua error " + status : Marshal.PtrToStringUTF8(pointer, (int)length);
            }
            finally { close(state); }
        }
        public void Dispose() => NativeLibrary.Free(library);
    }
}
