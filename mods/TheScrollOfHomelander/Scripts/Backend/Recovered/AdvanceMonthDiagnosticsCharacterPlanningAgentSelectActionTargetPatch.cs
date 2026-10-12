using System;
using GameData.ActionPlanning.MonthlyAI;
using GameData.ActionPlanning.MonthlyAI.Node;
using GameData.Common;
using HarmonyLib;

namespace BetterTaiwuScroll.Backend;

[HarmonyPatch(typeof(CharacterPlanningAgent), "SelectActionTarget", new Type[]
{
	typeof(DataContext),
	typeof(PlanningGoalNode),
	typeof(PlanningActionNode),
	typeof(ContextArgGroupHandle),
	typeof(bool)
})]
internal static class AdvanceMonthDiagnosticsCharacterPlanningAgentSelectActionTargetPatch
{
	private static void Prefix(out long __state)
	{
		__state = AdvanceMonthDiagnosticsRecorder.BeginActionPlanningDetail();
	}

	private static Exception Finalizer(long __state, Exception __exception)
	{
		AdvanceMonthDiagnosticsRecorder.EndActionPlanningDetail("CharacterPlanningAgent.SelectActionTarget", __state, __exception);
		return __exception;
	}
}

