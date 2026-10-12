using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

return Checks.Run(args);

internal static class Checks
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static Assembly _harmony = null!;
    private static readonly List<string> Errors = new();

    internal static int Run(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine("dotnet run --project tools/BackendPatchChecks -- --game-libs <Backend directory> --plugin <Backend DLL> [--report <JSON path>]");
            Console.WriteLine("Resolves the built plugin's Harmony targets and emits combined wrappers using the installed backend Harmony/MonoMod. Does not install detours, invoke gameplay methods, load settings, or initialize the plugin.");
            return args.Length == 0 ? 2 : 0;
        }

        try
        {
            var options = Parse(args);
            var gameLibs = Path.GetFullPath(options["--game-libs"]);
            var pluginPath = Path.GetFullPath(options["--plugin"]);
            foreach (var name in new[] { "GameData.dll", "GameData.Shared.dll", "TaiwuModdingLib.dll", "0Harmony.dll", "MonoMod.Utils.dll" })
                Require(File.Exists(Path.Combine(gameLibs, name)), "Missing backend dependency: " + name);
            Require(File.Exists(pluginPath), "Plugin DLL does not exist: " + pluginPath);
            // Framework assemblies remain in the host .NET 8 runtime. All game,
            // Harmony and MonoMod references come from this one backend directory.
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                var path = Path.Combine(gameLibs, name.Name + ".dll");
                return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
            };
            _harmony = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameLibs, "0Harmony.dll"));
            var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameLibs, "GameData.dll"));
            var plugin = AssemblyLoadContext.Default.LoadFromAssemblyPath(pluginPath);
            var harmonyType = Harmony("Harmony");
            var harmony = Activator.CreateInstance(harmonyType, "homelander.backend.offline-check")!;
            var groups = plugin.GetType("BetterTaiwuScroll.Shared.ModPatchGroups", throwOnError: true)!;
            var classify = groups.GetMethod("Classify", Static)!;
            var patchMarker = Harmony("HarmonyPatch");
            var classes = plugin.GetTypes().Where(type => type.IsDefined(patchMarker, false))
                .Select(type => (Type: type, Group: (string)classify.Invoke(null, new object[] { type })!))
                .OrderBy(entry => entry.Group, StringComparer.Ordinal)
                .ThenBy(entry => entry.Type.FullName, StringComparer.Ordinal).ToArray();
            Require(classes.Length > 0, "No backend Harmony patch classes discovered.");
            var records = new List<PatchRecord>();
            var jobs = new Dictionary<MethodBase, List<Hook>>();
            Console.WriteLine("Plugin SHA256 " + Hash(pluginPath));
            Console.WriteLine("GameData SHA256 " + Hash(game.Location));
            Console.WriteLine("Backend Harmony " + _harmony.GetName().Version + " SHA256 " + Hash(_harmony.Location));

            foreach (var (type, group) in classes)
            {
                var record = new PatchRecord(type.FullName!, group);
                records.Add(record);
                try
                {
                    CheckGroup(type, group);
                    var processor = harmonyType.GetMethod("CreateClassProcessor", Instance, null, new[] { typeof(Type) }, null)!.Invoke(harmony, new object[] { type })!;
                    var prepare = Auxiliary(processor, "HarmonyPrepare");
                    record.Prepare = prepare == null ? "absent" : InvokePrepare(prepare) ? "true" : "false";
                    if (record.Prepare == "false")
                    {
                        record.Status = "skipped-by-prepare";
                        Console.WriteLine("SKIP " + type.Name + " (Prepare=false)");
                        continue;
                    }

                    var descriptors = ((IEnumerable)Field(processor, "patchMethods")!).Cast<object>().Select(value =>
                    {
                        var info = Field(value, "info")!;
                        var kind = Field(value, "type")!.ToString()!;
                        var method = (MethodInfo)Field(info, "method")!;
                        Require(kind is "Prefix" or "Postfix" or "Transpiler" or "Finalizer" or "ILManipulator",
                            "Unsupported hook kind: " + kind);
                        return new Hook(type.FullName!, group, kind, method, info);
                    }).ToArray();
                    Require(descriptors.Length > 0, "Patch class has no hook methods.");
                    record.Hooks = descriptors.Select(hook => new HookRecord(hook.Kind, Signature(hook.Method))).ToList();
                    var bulk = ((IEnumerable)processor.GetType().GetMethod("GetBulkMethods", Instance)!.Invoke(processor, null)!)
                        .Cast<MethodBase>().ToArray();
                    var classJobs = new Dictionary<MethodBase, List<Hook>>();
                    foreach (var hook in descriptors)
                    {
                        if (bulk.Length > 0)
                        {
                            Require(Field(hook.Info, "methodName") == null && Field(hook.Info, "argumentTypes") == null,
                                "Bulk targets cannot be combined with per-method target annotations.");
                            Require(Field(hook.Info, "methodType")?.ToString() is null or "Normal",
                                "Bulk targets have an unsupported method type annotation.");
                        }
                        var targets = bulk.Length > 0 ? bulk : new[] { Resolve(hook.Info) };
                        foreach (var target in targets)
                        {
                            Require(target != null, "Undefined target method for " + Signature(hook.Method) + "; " + Requested(hook.Info));
                            Require(target!.Module.Assembly.Location.StartsWith(gameLibs + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                                "Target is outside the selected backend: " + Signature(target));
                            Require(target.GetMethodBody() != null, "Target has no managed method body: " + Signature(target));
                            if (!classJobs.TryGetValue(target, out var hooks)) classJobs[target] = hooks = new();
                            hooks.Add(hook);
                        }
                    }

                    foreach (var (target, hooks) in classJobs)
                    {
                        if (prepare != null && !InvokePrepare(prepare)) continue;
                        CheckParameters(type, target, hooks);
                        record.Targets.Add(new TargetRecord(target.Module.Assembly.GetName().Name!, Signature(target), $"0x{target.MetadataToken:X8}"));
                        if (!jobs.TryGetValue(target, out var combined)) jobs[target] = combined = new();
                        combined.AddRange(hooks);
                    }
                    Require(record.Targets.Count > 0, "No targets resolved for an enabled patch class.");
                    record.Status = "resolved";
                    Console.WriteLine($"RESOLVED [{group}] {type.Name}: {record.Targets.Count} target(s)");
                }
                catch (Exception error)
                {
                    record.Status = "failed";
                    record.Error = Unwrap(error).Message;
                    Fail(type.Name + ": " + record.Error);
                }
            }

            var wrappers = new List<WrapperRecord>();
            foreach (var (target, hooks) in jobs)
            {
                var record = new WrapperRecord(Signature(target), hooks.Select(hook => hook.PatchClass).Distinct().ToArray());
                wrappers.Add(record);
                try
                {
                    GenerateWrapper(target, hooks);
                    record.Status = "generated";
                }
                catch (Exception error)
                {
                    record.Status = "failed";
                    record.Error = Unwrap(error).ToString();
                    Fail("Wrapper " + Signature(target) + ": " + Unwrap(error).Message);
                }
            }

            var patched = ((IEnumerable)harmonyType.GetMethod("GetAllPatchedMethods", Static)!.Invoke(null, null)!).Cast<MethodBase>().ToArray();
            Require(patched.Length == 0, "Offline check unexpectedly registered live Harmony patches.");
            var fingerprints = AssemblyLoadContext.Default.Assemblies
                .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                .Where(assembly => assembly == plugin || assembly.Location.StartsWith(gameLibs + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)
                .Select(assembly => new { path = assembly.Location, identity = assembly.FullName, sha256 = Hash(assembly.Location) }).ToArray();
            var report = new
            {
                success = Errors.Count == 0,
                mode = "target-resolution-and-wrapper-generation-no-detours",
                runtime = RuntimeInformation.FrameworkDescription,
                pluginPath,
                gameLibs,
                fingerprints,
                patchClasses = records,
                wrappers,
                errors = Errors,
                limits = "Prepare, TargetMethod(s), Transpiler, and ILManipulator callbacks execute in this isolated tool process. No gameplay method, Prefix/Postfix/Finalizer, plugin initialization, settings loader, native detour or game process is run. Generated wrappers are not invoked or force-JITed; generation does not establish JIT validity, in-game behavior or native detour compatibility."
            };
            if (options.TryGetValue("--report", out var reportPath))
            {
                reportPath = Path.GetFullPath(reportPath);
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("REPORT " + reportPath);
            }
            Console.WriteLine($"{(Errors.Count == 0 ? "PASS" : "FAIL")}: {records.Count} patch classes, {records.Count(record => record.Status == "skipped-by-prepare")} Prepare skips, {records.Sum(record => record.Targets.Count)} class/target bindings, {wrappers.Count(record => record.Status == "generated")}/{wrappers.Count} combined wrappers generated, {Errors.Count} error(s), {patched.Length} live Harmony patches.");
            Console.WriteLine("Scope: target resolution and wrapper generation only; no native detours installed and no gameplay methods invoked.");
            return Errors.Count == 0 ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + Unwrap(error));
            return 1;
        }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
            if (i + 1 >= args.Length || args[i] is not ("--game-libs" or "--plugin" or "--report") || !options.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Expected --game-libs <Backend directory> --plugin <Backend DLL> [--report <JSON path>].");
        Require(options.ContainsKey("--game-libs") && options.ContainsKey("--plugin"), "Both --game-libs and --plugin are required.");
        return options;
    }

    private static void CheckGroup(Type type, string group)
    {
        var expected = type.Name.StartsWith("AutoMonthlyPurchase", StringComparison.Ordinal) ? "monthly-purchase"
            : type.Name.StartsWith("AutoCultivation", StringComparison.Ordinal) ? "cultivation"
            : type.Name.StartsWith("AdvanceMonthDiagnostics", StringComparison.Ordinal) ? "month-diagnostics" : null;
        // Record a grouping failure but still resolve its targets, so an old DLL
        // reports both the rollback exposure and its exact missing API signature.
        if (expected != null && group != expected) Fail(type.Name + $": expected isolated group '{expected}', actual '{group}'.");
    }

    private static MethodInfo? Auxiliary(object processor, string name)
    {
        foreach (DictionaryEntry pair in (IDictionary)Field(processor, "auxilaryMethods")!)
            if (((Type)pair.Key).Name == name) return (MethodInfo)pair.Value!;
        return null;
    }

    private static bool InvokePrepare(MethodInfo prepare)
    {
        Require(prepare.IsStatic && prepare.GetParameters().Length == 0 && prepare.ReturnType == typeof(bool),
            "This check supports reviewed parameterless bool Prepare callbacks only: " + Signature(prepare));
        return (bool)prepare.Invoke(null, null)!;
    }

    private static MethodBase? Resolve(object info) => (MethodBase?)Harmony("PatchTools")
        .GetMethod("GetOriginalMethod", Static)!.Invoke(null, new[] { info });

    private static string Requested(object info)
    {
        var owner = Field(info, "declaringType") as Type;
        var parameters = Field(info, "argumentTypes") as Type[];
        return "requested " + owner?.FullName + "." + Field(info, "methodName") + "(" +
            (parameters == null ? "unspecified parameters" : string.Join(", ", parameters.Select(TypeName))) + ")";
    }

    private static void CheckParameters(Type patch, MethodBase target, List<Hook> hooks)
    {
        var state = hooks.Where(hook => hook.Kind == "Prefix").SelectMany(hook => hook.Method.GetParameters())
            .SingleOrDefault(parameter => parameter.Name == "__state");
        foreach (var hook in hooks.Where(hook => hook.Kind is "Prefix" or "Postfix" or "Finalizer"))
        foreach (var parameter in hook.Method.GetParameters())
        {
            var type = ValueType(parameter.ParameterType);
            if (parameter.Name == "__state")
            {
                Require(state != null && ValueType(state.ParameterType) == type,
                    patch.Name + " has mismatched __state between hooks.");
                continue;
            }
            Type source;
            switch (parameter.Name)
            {
                case "__instance":
                    Require(!target.IsStatic, "Static target has no __instance.");
                    source = target.DeclaringType!;
                    break;
                case "__result":
                    source = target is MethodInfo method ? method.ReturnType : typeof(void);
                    Require(source != typeof(void), "Void target has no __result.");
                    break;
                case "__exception": source = typeof(Exception); break;
                case "__originalMethod": source = typeof(MethodBase); break;
                case "__runOriginal": source = typeof(bool); break;
                case "__args": source = typeof(object[]); break;
                default:
                    var original = target.GetParameters().SingleOrDefault(candidate => candidate.Name == parameter.Name);
                    Require(original != null, patch.Name + " injects missing original parameter '" + parameter.Name + "' into " + Signature(target));
                    source = ValueType(original!.ParameterType);
                    break;
            }
            Require(parameter.ParameterType.IsByRef ? source == type : type.IsAssignableFrom(source),
                patch.Name + " injects incompatible parameter '" + parameter.Name + "': " + TypeName(parameter.ParameterType) + " from " + TypeName(source));
        }
    }

    private static void GenerateWrapper(MethodBase target, List<Hook> hooks)
    {
        var patchInfo = Activator.CreateInstance(Harmony("PatchInfo"))!;
        foreach (var hook in hooks)
        {
            var methodName = hook.Kind switch
            {
                "Prefix" => "AddPrefixes", "Postfix" => "AddPostfixes", "Transpiler" => "AddTranspilers",
                "Finalizer" => "AddFinalizers", "ILManipulator" => "AddILManipulators", _ => throw new InvalidOperationException(hook.Kind)
            };
            var array = Array.CreateInstance(Harmony("HarmonyMethod"), 1);
            array.SetValue(hook.Info, 0);
            patchInfo.GetType().GetMethod(methodName, Instance)!.Invoke(patchInfo, new object[] { "homelander.backend.offline-check." + hook.Group, array });
        }
        var monoMod = AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("MonoMod.Utils"));
        var dmdType = monoMod.GetType("MonoMod.Utils.DynamicMethodDefinition", true)!;
        using var dmd = (IDisposable)dmdType.GetConstructor(new[] { typeof(MethodBase) })!.Invoke(new object[] { target });
        var definition = dmdType.GetProperty("Definition", Instance)!.GetValue(dmd)!;
        var contextType = monoMod.GetType("MonoMod.Cil.ILContext", true)!;
        using var context = (IDisposable)Activator.CreateInstance(contextType, definition)!;
        var manipulate = Harmony("Public.Patching.HarmonyManipulator").GetMethods(Static)
            .Single(method => method.Name == "Manipulate" && method.GetParameters().Length == 3);
        manipulate.Invoke(null, new object[] { target, patchInfo, context });
        Require(dmdType.GetMethod("Generate", Instance, null, Type.EmptyTypes, null)!.Invoke(dmd, null) is MethodInfo,
            "Harmony/MonoMod returned no generated wrapper.");
    }

    private static object? Field(object instance, string name) => (instance.GetType().GetField(name, Instance)
        ?? throw new MissingFieldException(instance.GetType().FullName, name)).GetValue(instance);
    private static Type Harmony(string name) => _harmony.GetType("HarmonyLib." + name, throwOnError: true)!;
    private static Type ValueType(Type type) => type.IsByRef ? type.GetElementType()! : type;
    private static string TypeName(Type type) => type.FullName ?? type.ToString();
    private static string Signature(MethodBase method) =>
        (method is MethodInfo info ? TypeName(info.ReturnType) + " " : "") + method.DeclaringType?.FullName + "." + method.Name +
        "(" + string.Join(", ", method.GetParameters().Select(parameter => TypeName(parameter.ParameterType) + " " + parameter.Name)) + ")";
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static Exception Unwrap(Exception error)
    {
        while (error is TargetInvocationException { InnerException: not null }) error = error.InnerException;
        return error;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Fail(string message) { Errors.Add(message); Console.Error.WriteLine("FAIL " + message); }

    private sealed record Hook(string PatchClass, string Group, string Kind, MethodInfo Method, object Info);
    private sealed record HookRecord(string Kind, string Signature);
    private sealed record TargetRecord(string Assembly, string Signature, string Token);
    private sealed record PatchRecord(string PatchClass, string Group)
    {
        public string Status { get; set; } = "pending";
        public string Prepare { get; set; } = "unknown";
        public List<HookRecord> Hooks { get; set; } = new();
        public List<TargetRecord> Targets { get; set; } = new();
        public string? Error { get; set; }
    }
    private sealed record WrapperRecord(string Target, string[] PatchClasses)
    {
        public string Status { get; set; } = "pending";
        public string? Error { get; set; }
    }
}
