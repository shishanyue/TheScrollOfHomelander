using System;
using System.Collections.Generic;
using BetterTaiwuScroll.Frontend;
using GameData.Domains.Building;

// Uses the installed game's result structs. No Unity process, game state or saves.
var cases = 0;
var recipes = new List<short> { 182, 183 };
void Check(string name, bool expected, MakeResult result, sbyte type = 7, short manual = -1)
{
    var actual = MakeResultValidation.IsUsable(result, type, recipes, manual);
    if (actual != expected) throw new Exception(name + ": expected " + expected + ", got " + actual);
    cases++;
}
MakeResult Result(MakeResultStage stage) => new MakeResult(0, new[] { stage }, -1, false);

if (default(MakeResult).TargetResultStage.TemplateId != 0)
    throw new Exception("The installed game's default result contract changed.");
Check("missing preview must not become wild fruit", false, default);
Check("stopped medicine preview must not become poison template zero", false, default, type: 8);
Check("uninitialized stage", false, Result(default));
Check("out-of-range target stage", false, new MakeResult(3, new MakeResultStage[1], -1, false));
Check("valid cooked food", true, Result(new MakeResultStage(10, true, 7, 9, 182)));
Check("insufficient attainment", false, Result(new MakeResultStage(10, false, 7, 9, 182)));
Check("stale recipe", false, Result(new MakeResultStage(10, true, 7, 9, 200)));
Check("wrong item type", false, Result(new MakeResultStage(10, true, 8, 9, 182)));
Check("manual subtype changed", false, Result(new MakeResultStage(10, true, 7, 9, 182)), manual: 183);
Check("manual subtype retained", true, Result(new MakeResultStage(10, true, 7, 9, 182)), manual: 182);
Check("valid template zero is not globally blacklisted", true, Result(new MakeResultStage(10, true, 7, 0, 182)));
Check("valid medicine template zero remains craftable", true, Result(new MakeResultStage(10, true, 8, 0, 182)), type: 8);
Check("fresh medicine preview retains selected recipe", true, Result(new MakeResultStage(10, true, 8, 18, 182)), type: 8, manual: 182);
Check("stale medicine subtype cannot replace selected recipe", false, Result(new MakeResultStage(10, true, 8, 0, 183)), type: 8, manual: 182);
Check("valid random pool", true, Result(new MakeResultStage(10, true, 7,
    new List<short> { 9, 18 }, new List<short> { 182, 183 })));
Check("empty random pool", false, Result(new MakeResultStage(10, true, 7,
    new List<short>(), new List<short>())));
Check("mismatched random pool", false, Result(new MakeResultStage(10, true, 7,
    new List<short> { 9, 18 }, new List<short> { 182 })));
Check("stale random pool subtype", false, Result(new MakeResultStage(10, true, 7,
    new List<short> { 9, 18 }, new List<short> { 182, 200 })));
Check("random pool cannot override manual choice", false, Result(new MakeResultStage(10, true, 7,
    new List<short> { 9, 18 }, new List<short> { 182, 183 })), manual: 182);
Console.WriteLine($"PASS: {cases} crafting result regression checks against installed GameData.Shared.dll.");
