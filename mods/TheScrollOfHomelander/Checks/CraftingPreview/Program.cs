using System;
using BetterTaiwuScroll.Frontend;

// Exercises the exact callback/refresh barrier used by frontend crafting, with no Unity.
var cases = 0;
void Check(string name, bool actual)
{
    if (!actual) throw new Exception(name);
    cases++;
}

var barrier = new MakePreviewBarrier();
Check("first recovery queues", barrier.TryQueueRefresh());
Check("repeated confirmation coalesces the pending recovery", !barrier.TryQueueRefresh());
var oldPreview = barrier.Begin();
oldPreview.AddCallback();
oldPreview.AddCallback();
Check("immediate confirmation waits for every response", !oldPreview.IsReady);
Check("first response accepted", oldPreview.TryAcceptCallback());
Check("partial preview cannot confirm", !oldPreview.IsReady);
Check("final response accepted", oldPreview.TryAcceptCallback());
Check("complete preview can confirm", oldPreview.IsReady);

barrier.Invalidate();
Check("stopping invalidates the completed preview", !oldPreview.IsReady);
Check("old response cannot refill cache after stop", !oldPreview.TryAcceptCallback());
Check("stop queues a fresh recovery", barrier.TryQueueRefresh());
Check("stop followed by immediate confirmation does not queue twice", !barrier.TryQueueRefresh());
var afterStop = barrier.Begin();
afterStop.AddCallback();
Check("new preview still waits", !afterStop.IsReady);
Check("old callback cannot complete the new preview", !oldPreview.TryAcceptCallback() && !afterStop.IsReady);
Check("current callback completes the new preview", afterStop.TryAcceptCallback() && afterStop.IsReady);

var changedRecipe = barrier.Begin();
changedRecipe.AddCallback();
Check("a new request supersedes the previous recipe", !afterStop.IsReady);
changedRecipe.MarkStale();
Check("changed options reject remaining callbacks", !changedRecipe.TryAcceptCallback() && !changedRecipe.IsReady);
Check("stale recipe schedules one retry", barrier.TryQueueRefresh());
Check("repeated stale callbacks do not schedule a retry storm", !barrier.TryQueueRefresh());

barrier.Invalidate();
Check("page closure/session reset releases queued recovery", barrier.TryQueueRefresh());
Console.WriteLine($"PASS: {cases} crafting preview lifetime checks; no game or Unity process.");
