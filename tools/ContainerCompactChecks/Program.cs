using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;

return Checks.Run(args);

internal static class Checks
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static int _assertions;
    private static Assembly _game = null!;
    private static Assembly _plugin = null!;

    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
            {
                Console.WriteLine("Usage: dotnet run --project tools/ContainerCompactChecks -- --game-libs <frontend Managed directory> --plugin <frontend plugin DLL>");
                Console.WriteLine("The two paths may also be supplied as positional arguments. No game process, Harmony patches or settings writer are started.");
                return args.Length == 0 ? 2 : 0;
            }
            var (gameLibs, pluginPath) = ParsePaths(args);
            var gamePath = Path.Combine(gameLibs, "Assembly-CSharp.dll");
            Require(File.Exists(gamePath), "frontend Assembly-CSharp.dll exists");
            Require(File.Exists(pluginPath), "frontend plugin DLL exists");
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                foreach (var directory in new[] { gameLibs, Path.GetDirectoryName(pluginPath)! })
                {
                    var path = Path.Combine(directory, name.Name + ".dll");
                    if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                }
                return null;
            };
            _game = AssemblyLoadContext.Default.LoadFromAssemblyPath(gamePath);
            _plugin = AssemblyLoadContext.Default.LoadFromAssemblyPath(pluginPath);
            Console.WriteLine("Assembly-CSharp SHA256 " + Hash(gamePath));
            Console.WriteLine("Frontend plugin SHA256 " + Hash(pluginPath));

            var owners = new[]
            {
                Game("Game.Components.ListStyleGeneralScroll.Item.CardStyleGroupedScroll"),
                Game("Game.Components.Common.CombatSkillSelect"),
                Game("Game.Views.CharacterMenu.EquipCombatSkillGroupedSkillScroll"),
            };
            var rows = new[]
            {
                Game("Game.Components.ListStyleGeneralScroll.Item.CardGroupedRowItem"),
                Game("Game.Components.Common.CombatSkillGroupedRowItem"),
                Game("Game.Views.CharacterMenu.EquipCombatSkillGroupRowItem"),
            };
            CheckApis(owners, rows);
            CheckPatchTargets();
            CheckScale();
            CheckHighlightScale();
            foreach (var owner in owners) CheckHeightAccounting(owner);
            CheckSkillRows(owners[1], hasTemplateMap: true);
            CheckSkillRows(owners[2], hasTemplateMap: false);
            Console.WriteLine($"PASS: {_assertions} assertions. No game process, Unity scene, Harmony patch installation or user settings writer was started.");
            return 0;
        }
        catch (Exception error)
        {
            while (error is TargetInvocationException { InnerException: not null }) error = error.InnerException;
            Console.Error.WriteLine("FAIL: " + error.Message);
            return 1;
        }
    }

    private static (string GameLibs, string Plugin) ParsePaths(string[] args)
    {
        if (args.Length == 2 && !args[0].StartsWith("--"))
            return (Path.GetFullPath(args[0]), Path.GetFullPath(args[1]));
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i] is not ("--game-libs" or "--plugin") || !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Supply exactly --game-libs <frontend Managed directory> and --plugin <frontend plugin DLL>.");
        }
        if (!values.TryGetValue("--game-libs", out var gameLibs) || !values.TryGetValue("--plugin", out var plugin))
            throw new ArgumentException("Both --game-libs and --plugin are required.");
        return (Path.GetFullPath(gameLibs), Path.GetFullPath(plugin));
    }

    private static void CheckApis(Type[] owners, Type[] rows)
    {
        var support = Mod("GroupedContainerCompactSupport");
        for (var i = 0; i < owners.Length; i++)
        {
            var owner = owners[i];
            var row = rows[i];
            FieldType(owner, "scrollView", "UnityEngine.UI.LoopVerticalScrollRect");
            Require(Field(owner, "rowTemplate").FieldType == row, owner.Name + " uses its current grouped row template");
            FieldType(owner, i == 0 ? "_cardCountPerRow" : "_itemsPerLine", "System.Int32");
            FieldType(owner, "_contentHeight", "System.Single");
            FieldType(row, "contentRoot", "UnityEngine.GameObject");
            FieldType(row, "contentPreferredHeight", "System.Single");
            FieldType(row, "_contentInitialized", "System.Boolean");
            FieldType(row, i == 0 ? "contentCardItem" : "contentItemTemplate", i switch
            {
                0 => "Game.Components.ListStyleGeneralScroll.Item.CardItem",
                1 => "Game.Components.Common.CombatSkillSelectable",
                _ => "Game.Views.CharacterMenu.EquipCombatSkillItem",
            });
            if (i > 0) FieldType(row, "itemCountPerLine", "System.Int32");
            var set = row.GetMethods(Instance).Where(method => method.Name == "Set").ToArray();
            Require(set.Length == 1, row.Name + ".Set is unambiguous");
            Console.WriteLine($"API {owner.FullName} / {row.FullName}.Set 0x{set[0].MetadataToken:X8}");

            // Resolve the Mod's actual cached fields, rather than validating only
            // duplicate field-name strings maintained by this check.
            var api = support.GetField(new[] { "CardApi", "SkillApi", "EquipApi" }[i], Static)?.GetValue(null)
                ?? throw new Exception("Mod owner API descriptor is missing for " + owner.Name);
            foreach (var pair in new[]
            {
                ("RowTemplate", owner, "rowTemplate"), ("ScrollView", owner, "scrollView"),
                ("OwnerColumns", owner, i == 0 ? "_cardCountPerRow" : "_itemsPerLine"),
                ("OwnerContentHeight", owner, "_contentHeight"), ("FlatRows", owner, "_flatRows"), ("ContentRoot", row, "contentRoot"),
                ("ContentItem", row, i == 0 ? "contentCardItem" : "contentItemTemplate"),
                ("ContentHeight", row, "contentPreferredHeight"), ("ContentInitialized", row, "_contentInitialized"),
            })
            {
                var resolved = api.GetType().GetField(pair.Item1, Instance)?.GetValue(api) as FieldInfo;
                var expected = Field(pair.Item2, pair.Item3);
                Require(resolved != null && resolved.Module == expected.Module && resolved.MetadataToken == expected.MetadataToken,
                    owner.Name + " Mod cache resolves " + pair.Item1);
            }
            Require(api.GetType().GetField("OwnerType", Instance)?.GetValue(api) as Type == owner,
                owner.Name + " Mod cache resolves owner type");
            Require(api.GetType().GetField("RowType", Instance)?.GetValue(api) as Type == row,
                owner.Name + " Mod cache resolves grouped row type");
            if (i < 2)
            {
                CheckCachedMethod(api, "Rebuild", owner, i == 0 ? "RebuildFlatRows" : "RebuildAndRefresh",
                    i == 0 ? Type.EmptyTypes : new[] { typeof(bool) });
            }
            if (i == 0) CheckCachedMethod(api, "QueueRefresh", owner, "QueueRefreshScrollView", new[] { typeof(bool) });
        }
        var observer = Mod("GroupedContainerDimensionObserver");
        Require(!observer.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "HarmonyLib.HarmonyPatch"),
            "dimension observer has no Harmony patch targets");
        Console.WriteLine("PASS current grouped owner/row APIs and Mod field caches");
    }

    private static void CheckCachedMethod(object api, string cacheName, Type owner, string methodName, Type[] parameters)
    {
        var resolved = api.GetType().GetField(cacheName, Instance)?.GetValue(api) as MethodInfo;
        var expected = owner.GetMethod(methodName, Instance, null, parameters, null);
        Require(resolved != null && expected != null && resolved.Module == expected.Module && resolved.MetadataToken == expected.MetadataToken,
            owner.Name + " Mod cache resolves " + cacheName + " to installed " + methodName);
    }

    private static void CheckPatchTargets()
    {
        var expected = new[]
        {
            ("GroupedContainerCompactOwnerPatch", 6), ("GroupedContainerCompactRowPatch", 3),
        };
        var resolved = new HashSet<MethodBase>();
        foreach (var (name, count) in expected)
        {
            var patch = Mod(name);
            Require(patch.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "HarmonyLib.HarmonyPatch"), name + " has Harmony marker");
            var targetMethods = patch.GetMethod("TargetMethods", Static) ?? throw new Exception(name + ".TargetMethods missing");
            var targets = ((IEnumerable)targetMethods.Invoke(null, null)!).Cast<MethodBase?>().ToArray();
            Require(targets.Length == count && targets.All(target => target != null), name + " resolves every target");
            Require(targets.Distinct().Count() == count, name + " has no duplicate target");
            foreach (var target in targets)
            {
                Require(target!.Module.Assembly == _game, name + " targets current frontend game assembly");
                resolved.Add(target);
                CheckInjectedParameters(patch, target);
                Console.WriteLine($"TARGET {target.DeclaringType!.FullName}.{target.Name} 0x{target.MetadataToken:X8}");
            }
        }
        var sectionsPatch = Mod("GroupedContainerCompactEquipSectionsPatch");
        var declaration = sectionsPatch.CustomAttributes.Single(attribute => attribute.AttributeType.FullName == "HarmonyLib.HarmonyPatch");
        Require(declaration.ConstructorArguments.Count == 2 && declaration.ConstructorArguments[0].Value is Type,
            "equipment section patch declares an exact owner and method");
        var ownerType = (Type)declaration.ConstructorArguments[0].Value!;
        var methodName = (string)declaration.ConstructorArguments[1].Value!;
        var sections = ownerType.GetMethods(Instance).Single(method => method.Name == methodName);
        Require(ownerType == Game("Game.Views.CharacterMenu.EquipCombatSkillGroupedSkillScroll") && methodName == "SetSections",
            "equipment section patch resolves current SetSections");
        CheckInjectedParameters(sectionsPatch, sections);
        resolved.Add(sections);
        Require(resolved.Count == 10, "all ten grouped compact Harmony targets resolve without patch installation");
        Console.WriteLine("PASS grouped compact Harmony targets and injected parameter signatures");
    }

    private static void CheckInjectedParameters(Type patch, MethodBase target)
    {
        foreach (var method in patch.GetMethods(Static).Where(method => method.Name is "Prefix" or "Postfix"))
        foreach (var parameter in method.GetParameters())
        {
            if (parameter.Name == "__instance")
                Require(parameter.ParameterType.IsAssignableFrom(target.DeclaringType), patch.Name + " accepts target instance");
            else if (parameter.Name == "__originalMethod")
                Require(parameter.ParameterType == typeof(MethodBase), patch.Name + " accepts Harmony original method metadata");
            else
            {
                var original = target.GetParameters().SingleOrDefault(candidate => candidate.Name == parameter.Name);
                Require(original?.ParameterType == parameter.ParameterType, patch.Name + " injects current parameter " + parameter.Name);
            }
        }
    }

    private static void CheckScale()
    {
        var calculate = Mod("GroupedContainerCompactSupport").GetMethod("CalculateScale", Static, null,
            new[] { typeof(float), typeof(float), typeof(float), typeof(int), typeof(int) }, null)
            ?? throw new Exception("GroupedContainerCompactSupport.CalculateScale signature missing");
        float Scale(float width, float cell = 240f, float spacing = 8f, int padding = 20, int columns = 6)
            => (float)calculate.Invoke(null, new object[] { width, cell, spacing, padding, columns })!;
        Equal(0.64f, Scale(960f), "six cards fit within a 960 px viewport including spacing and padding");
        Equal(1f, Scale(2000f), "roomy viewport preserves native scale");
        Equal(Scale(960f), Scale(960f), "repeated calculation does not accumulate shrinking");
        foreach (var width in new[] { 320f, 640f, 960f, 1440f })
        foreach (var columns in new[] { 1, 3, 5, 8, 12 })
        {
            var scale = Scale(width, columns: columns);
            var nativeWidth = 240f * columns + 8f * (columns - 1) + 20;
            Require(float.IsFinite(scale) && scale > 0f && scale <= 1f && nativeWidth * scale <= width + 0.001f,
                $"{columns} columns fit width {width} without enlargement or clipping");
        }
        foreach (var width in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            Equal(1f, Scale(width), "invalid viewport width leaves native layout");
        foreach (var cell in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            Equal(1f, Scale(960f, cell), "invalid cell width leaves native layout");
        foreach (var spacing in new[] { -1f, float.NaN, float.PositiveInfinity })
            Equal(1f, Scale(960f, spacing: spacing), "invalid spacing leaves native layout");
        Equal(1f, Scale(960f, padding: -1), "invalid padding leaves native layout");
        Equal(1f, Scale(960f, columns: 0), "zero columns leaves native layout");
        Equal(1f, Scale(960f, columns: -1), "negative columns leaves native layout");
        Console.WriteLine("PASS width, spacing, padding, column count, repeated-input and invalid-size scale checks");
    }

    private static void CheckHeightAccounting(Type owner)
    {
        var instance = RuntimeHelpers.GetUninitializedObject(owner);
        var rowsField = Field(owner, "_flatRows");
        var rows = (IList)Activator.CreateInstance(rowsField.FieldType)!;
        var rowType = rowsField.FieldType.GetGenericArguments()[0];
        foreach (var isTitle in new[] { true, false, false })
        {
            var row = rowType.IsValueType ? Activator.CreateInstance(rowType)! : RuntimeHelpers.GetUninitializedObject(rowType);
            Field(rowType, "IsTitle").SetValue(row, isTitle);
            rows.Add(row);
        }
        rowsField.SetValue(instance, rows);
        Field(owner, "_titleHeight").SetValue(instance, 7f);
        Field(owner, "_contentHeight").SetValue(instance, 36f);
        var size = owner.GetMethod("GetItemsSize", Instance, null, new[] { typeof(int), typeof(int) }, null)!;
        float Height(int start, int end) => (float)size.Invoke(instance, new object[] { start, end })!;
        Equal(79f, Height(0, 3), owner.Name + " accounts for native title and content heights");
        Equal(7f, Height(0, 1), owner.Name + " preserves title height");
        Equal(72f, Height(1, 3), owner.Name + " accounts for content rows");
        Equal(36f, Height(2, 100), owner.Name + " clips range at row count");
        Equal(0f, Height(3, 100), owner.Name + " handles exhausted range");
        rows.Clear();
        Equal(0f, Height(0, 100), owner.Name + " handles empty list");
        Console.WriteLine("PASS managed height accounting: " + owner.Name);
    }

    private static void CheckHighlightScale()
    {
        var support = Mod("GroupedContainerCompactSupport");
        var calculate = support.GetMethod("CalculateHighlightScale", Static)
            ?? throw new Exception("GroupedContainerCompactSupport.CalculateHighlightScale missing");
        var parameters = calculate.GetParameters();
        Require(parameters.Length == 3 && parameters.All(parameter => parameter.ParameterType.FullName == "UnityEngine.Vector3")
            && calculate.ReturnType.FullName == "UnityEngine.Vector3", "highlight scale uses three Vector3 inputs and Vector3 output");
        var vectorType = calculate.ReturnType;
        object Vector(float x, float y, float z) => Activator.CreateInstance(vectorType, new object[] { x, y, z })!;
        void Check((float X, float Y, float Z) world, (float X, float Y, float Z) parent,
            (float X, float Y, float Z) fallback, (float X, float Y, float Z) expected, string label)
        {
            var result = calculate.Invoke(null, new[]
            {
                Vector(world.X, world.Y, world.Z), Vector(parent.X, parent.Y, parent.Z), Vector(fallback.X, fallback.Y, fallback.Z),
            })!;
            Equal(expected.X, (float)Field(vectorType, "x").GetValue(result)!, label + " x");
            Equal(expected.Y, (float)Field(vectorType, "y").GetValue(result)!, label + " y");
            Equal(expected.Z, (float)Field(vectorType, "z").GetValue(result)!, label + " z");
        }
        Check((0.64f, 0.64f, 1f), (1f, 1f, 1f), (1f, 1f, 1f), (0.64f, 0.64f, 1f),
            "focus mask reparenting retains compact card visual scale");
        Check((1.28f, 1.28f, 2f), (2f, 2f, 2f), (1f, 1f, 1f), (0.64f, 0.64f, 1f),
            "focus mask compensates for global UI scale");
        Check((1.28f, 0.96f, 0.5f), (2f, 3f, 0.25f), (1f, 1f, 1f), (0.64f, 0.32f, 2f),
            "focus mask compensates each parent axis independently");
        foreach (var parent in new[] { (0f, 1f, 1f), (1f, 0f, 1f), (1f, 1f, 0f), (0.00001f, 1f, 1f) })
            Check((0.64f, 0.64f, 1f), parent, (0.9f, 0.8f, 0.7f), (0.9f, 0.8f, 0.7f),
                "focus mask with collapsed parent retains fallback scale");
        Console.WriteLine("PASS focus mask reparenting, global UI scale, per-axis compensation and collapsed-parent fallback");
    }

    private static void CheckSkillRows(Type owner, bool hasTemplateMap)
    {
        var instance = RuntimeHelpers.GetUninitializedObject(owner);
        foreach (var name in hasTemplateMap ? new[] { "_flatRows", "_allSkills", "_templateIdToRowIndex" } : new[] { "_flatRows", "_allSkills" })
        {
            var field = Field(owner, name);
            field.SetValue(instance, Activator.CreateInstance(field.FieldType));
        }
        var build = owner.GetMethod("BuildFlatRows", Instance)!;
        var sectionsType = build.GetParameters()[0].ParameterType;
        var sectionType = sectionsType.GetGenericArguments()[0];
        var itemType = sectionType.GetGenericArguments()[0];
        var listType = typeof(List<>).MakeGenericType(itemType);
        var sections = (IList)Activator.CreateInstance(sectionsType)!;
        short nextId = 1;
        void Section(string header, int count, bool equipped)
        {
            var list = (IList)Activator.CreateInstance(listType)!;
            for (var i = 0; i < count; i++)
            {
                var item = itemType.IsValueType ? Activator.CreateInstance(itemType)! : RuntimeHelpers.GetUninitializedObject(itemType);
                if (hasTemplateMap) Field(itemType, "TemplateId").SetValue(item, nextId++);
                list.Add(item);
            }
            sections.Add(Activator.CreateInstance(sectionType, new object?[] { header, list, equipped, null }));
        }
        Section("Equipped", 11, true);
        Section("Available", 6, false);
        Section("Empty", 0, false);
        var flatRows = (IList)Field(owner, "_flatRows").GetValue(instance)!;
        var allSkills = (IList)Field(owner, "_allSkills").GetValue(instance)!;
        var rowType = flatRows.GetType().GetGenericArguments()[0];
        foreach (var columns in new[] { 1, 3, 5, 8, 12 })
        {
            Field(owner, "_itemsPerLine").SetValue(instance, columns);
            build.Invoke(instance, new object[] { sections });
            Require(allSkills.Count == 17, owner.Name + " retains all items at columns=" + columns);
            Require(flatRows.Count == 2 + (11 + columns - 1) / columns + (6 + columns - 1) / columns,
                owner.Name + " keeps headers and section boundaries at columns=" + columns);
            var covered = 0;
            foreach (var row in flatRows)
            {
                if ((bool)Field(rowType, "IsTitle").GetValue(row)!) continue;
                var start = (int)Field(rowType, "StartIndex").GetValue(row)!;
                var count = (int)Field(rowType, "ItemCount").GetValue(row)!;
                Require(start == covered && count > 0 && count <= columns && start + count <= allSkills.Count,
                    owner.Name + " content rows cover every item once at columns=" + columns);
                covered += count;
            }
            Require(covered == 17, owner.Name + " last partial rows retain all items at columns=" + columns);
            if (hasTemplateMap)
            {
                var map = (IDictionary)Field(owner, "_templateIdToRowIndex").GetValue(instance)!;
                Require(map.Count == 17, owner.Name + " rebuilds template index map");
                foreach (DictionaryEntry entry in map)
                {
                    var row = flatRows[(int)entry.Value!]!;
                    var start = (int)Field(rowType, "StartIndex").GetValue(row)!;
                    var count = (int)Field(rowType, "ItemCount").GetValue(row)!;
                    var id = (short)entry.Key;
                    Require(id - 1 >= start && id - 1 < start + count,
                        owner.Name + " selected template maps to its content row");
                }
            }
        }
        build.Invoke(instance, new object?[] { null });
        Require(flatRows.Count == 0 && allSkills.Count == 0, owner.Name + " clears stale rows on empty rebuild");
        if (hasTemplateMap)
            Require(((IDictionary)Field(owner, "_templateIdToRowIndex").GetValue(instance)!).Count == 0,
                owner.Name + " clears stale template mappings");
        Console.WriteLine("PASS managed section flattening, partial rows and refresh: " + owner.Name);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static Type Game(string name) => _game.GetType(name, throwOnError: true)!;
    private static Type Mod(string name) => _plugin.GetType("BetterTaiwuScroll.Frontend." + name, throwOnError: true)!;
    private static FieldInfo Field(Type type, string name) => type.GetField(name, Instance)
        ?? throw new Exception(type.FullName + "." + name + " field missing");
    private static void FieldType(Type type, string name, string expected)
        => Require(Field(type, name).FieldType.FullName == expected, type.Name + "." + name + " has current field type " + expected);
    private static void Equal(float expected, float actual, string label)
        => Require(float.IsFinite(actual) && Math.Abs(expected - actual) <= 0.00001f, label + $" (expected {expected}, actual {actual})");
    private static void Require(bool success, string label)
    {
        if (!success) throw new Exception(label);
        _assertions++;
    }
}
