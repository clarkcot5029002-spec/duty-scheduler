using Duty.Core;
using Duty.Solver;
using System.Text.Json;

var passed = 0;
Console.OutputEncoding = new System.Text.UTF8Encoding(false);
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL: " + label);
    passed++;
    Console.WriteLine("PASS: " + label);
}
void Reject(Action action, string label)
{
    try { action(); }
    catch (Exception e) when (e is ArgumentException or FormatException)
    { Check(true, label); return; }
    throw new Exception("FAIL: " + label);
}
var defaults = PreferenceMarks.CreateDefault();
Check(defaults.Length == Week.SlotCount, "default availability has twenty slots");
Check(Enumerable.Range(0, 20).Where(s => s != Week.SpecialSlot).All(s => defaults[s] == 1),
    "unmarked ordinary slots are available");
var defaultPerson = new Person("默认测试", 3, defaults);
Check(defaults[Week.SpecialSlot] == 0 && !defaultPerson.Special, "special shift remains explicit opt in");
Check(defaultPerson.Available == 19 && defaultPerson.AvailableMornings == 5, "default available count and mornings");
var independentDefaults = PreferenceMarks.CreateDefault();
independentDefaults[0] = 0;
Check(defaults[0] == 1, "default preference arrays are independent");
foreach (var (current, marker, expected) in new (byte, byte, byte)[]
{
    (1, 2, 2), (2, 2, 1), (0, 2, 2), (1, 0, 0), (0, 0, 1), (2, 0, 0)
})
    Check(PreferenceMarks.Toggle(current, marker) == expected, $"marker transition {current}/{marker}/{expected}");
Reject(() => PreferenceMarks.Toggle(1, 1), "normal state is not a selectable marker");
Reject(() => PreferenceMarks.Toggle(3, 0), "invalid current marker state");
Reject(() => PreferenceMarks.Toggle(1, 3), "invalid selected marker");
Check(SubmissionCodec.Decode(SubmissionCodec.Encode(defaultPerson)).Preferences.SequenceEqual(defaults),
    "default availability preserves PB1 codec");
var marked = PreferenceMarks.Toggle(1, 2);
marked = PreferenceMarks.Toggle(marked, 0);
marked = PreferenceMarks.Toggle(marked, 0);
Check(marked == 1, "priority replaced by unavailable then cancelled to available");
var names = new[] { "示例甲", "示例乙", "样例", "示例丁", "示例戊", "𠮷示例", "测试同学甲" };
var random = new Random(1607);
foreach (var name in names)
{
    foreach (var target in new[] { 3, 4 })
    {
        var preferences = Enumerable.Range(0, 20).Select(_ => (byte)random.Next(3)).ToArray();
        var original = new Person(name, target, preferences);
        var encoded = SubmissionCodec.Encode(original);
        var decoded = SubmissionCodec.Decode(encoded.ToLowerInvariant());
        Check(decoded.Name == name && decoded.Target == target && decoded.Preferences.SequenceEqual(preferences)
              && decoded.Special == original.Special && encoded.All(c => c < 128), "codec " + name + target);
        Reject(() => SubmissionCodec.Decode(encoded[..^1]), "truncated " + name + target);
        var altered = encoded.ToCharArray();
        altered[8] = altered[8] == 'A' ? 'B' : 'A';
        Reject(() => SubmissionCodec.Decode(new string(altered)), "CRC corruption " + name + target);
    }
}
for (var k = 0; k < 200; k++)
{
    var choices = Enumerable.Range(0, 20).Select(_ => (byte)random.Next(3)).ToArray();
    var person = new Person("随机测试" + k, k % 2 == 0 ? 3 : 4, choices);
    var result = SubmissionCodec.Decode(SubmissionCodec.Encode(person));
    Check(result.Preferences.SequenceEqual(choices) && result.Name == person.Name, "random round trip " + k);
}
Reject(() => new Person("", 3, new byte[20]), "empty name");
Reject(() => new Person("换\n行", 3, new byte[20]), "control name");
Reject(() => new Person("班量", 5, new byte[20]), "invalid quota");
Reject(() => SubmissionCodec.Decode("PB1-汉字"), "non ASCII code");
Reject(() => SubmissionCodec.Decode("PB2-AAAA"), "unsupported version");

var allFree = names.Take(5).Select((n, i) => new Person(n, 4,
    Enumerable.Range(0, 20).Select(s => (byte)(s / 4 == i ? 2 : 1)))).ToArray();
var solution = Scheduler.Solve(allFree, TimeSpan.FromSeconds(15));
var evaluation = ScheduleRules.Evaluate(allFree, solution.Schedule);
Check(!solution.Draft && evaluation.PersonalSatisfied, "five people normal feasibility");
Check(evaluation.Assigned == 20 && evaluation.Gaps == 1, "21 demand versus 20 available");
Check(solution.ProvenOptimal && solution.Stages.Count == 3, "lexicographic optimality");
Check(evaluation.Preferred == 20 && evaluation.Days == 5 && evaluation.Blocks == 5,
    "preferred maximum then day and block minimum");
Check(evaluation.People.All(p => p.Mornings == 1), "each morning exactly once with five people");

var noAvailability = new[] { new Person("全禁排", 4, new byte[20]) };
var emptyDraft = Scheduler.Solve(noAvailability, TimeSpan.FromSeconds(5));
Check(emptyDraft.Draft && emptyDraft.Schedule.Assigned == 0, "zero availability returns warned draft");
Check(!ScheduleRules.Evaluate(noAvailability, emptyDraft.Schedule).Issues.Any(i => i.Hard), "draft never violates unavailable");

var six = Enumerable.Range(1, 6).Select(i => new Person("同学" + i, 4, Enumerable.Repeat((byte)1, 20))).ToArray();
var sixDraft = Scheduler.Solve(six, TimeSpan.FromSeconds(15));
var sixEval = ScheduleRules.Evaluate(six, sixDraft.Schedule);
Check(sixDraft.Draft && !sixEval.PersonalSatisfied, "six people warn missing morning");
Check(sixEval.People.Count(p => p.Mornings > 0) == 5, "maximum five early shifts in draft");
Check(!sixEval.Issues.Any(i => i.Hard) && sixEval.Assigned == 21, "draft maximizes load without overbooking");
Check(sixDraft.Schedule.Cells[7].Count(n => n is not null) == 2, "special slot supports two people");

var excluded = allFree.Select(p => {
    var slots = p.CopyPreferences(); slots[7] = 0; return new Person(p.Name, p.Target, slots);
}).ToArray();
var excludedResult = Scheduler.Solve(excluded, TimeSpan.FromSeconds(15));
Check(excludedResult.Schedule.Cells[7].All(n => n is null), "special opt out never assigned");

var singleSlots = new byte[20];
singleSlots[0] = 1; singleSlots[1] = 2; singleSlots[2] = 2; singleSlots[4] = 2;
var single = new[] { new Person("优先验证", 3, singleSlots) };
var prefResult = Scheduler.Solve(single, TimeSpan.FromSeconds(5));
var prefEval = ScheduleRules.Evaluate(single, prefResult.Schedule);
Check(prefEval.Preferred == 3 && prefEval.Days == 2, "preferences outrank concentration");

var oneDay = new Person("午休连班", 3, [1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
var lunch = Scheduler.Solve([oneDay], TimeSpan.FromSeconds(5));
Check(ScheduleRules.Evaluate([oneDay], lunch.Schedule).Blocks == 1, "lunch does not split block");

var before = new Schedule();
before.Cells[0][0] = oneDay.Name;
Check(!ScheduleRules.TryPlace([oneDay], before, 4, 0, oneDay.Name, out _, out _), "manual rejects unavailable");
var two = new[] { new Person("甲", 3, Enumerable.Repeat((byte)1, 20)),
                  new Person("乙", 3, Enumerable.Repeat((byte)1, 20)) };
var swap = new Schedule(); swap.Cells[0][0] = "甲"; swap.Cells[1][0] = "乙";
Check(ScheduleRules.TrySwap(two, swap, 0, 0, 1, 0, out var swapped, out _)
      && swapped.Cells[0][0] == "乙" && swapped.Cells[1][0] == "甲", "atomic manual swap");
var history = new UndoHistory<string>(); history.Push("old");
Check(history.Undo("new") == "old" && history.Redo("old") == "new", "undo redo");

for (var caseIndex = 0; caseIndex < 12; caseIndex++)
{
    Person OraclePerson(string name)
    {
        var choices = new byte[20];
        var available = new HashSet<int> { 0, 4 };
        while (available.Count < 7) available.Add(random.Next(20));
        foreach (var s in available) choices[s] = (byte)random.Next(1, 3);
        return new Person(name, 3, choices);
    }
    var oraclePeople = new[] { OraclePerson("枚举甲"), OraclePerson("枚举乙") };
    List<int[]> Choices(Person person)
    {
        var available = Enumerable.Range(0, 20).Where(s => person.Preferences[s] > 0).ToArray();
        var selections = new List<int[]>();
        for (var a = 0; a < available.Length; a++)
            for (var b = a + 1; b < available.Length; b++)
                for (var c = b + 1; c < available.Length; c++)
                {
                    var chosen = new[] { available[a], available[b], available[c] };
                    if (chosen.Any(s => s % 4 == 0)) selections.Add(chosen);
                }
        return selections;
    }
    (int H, int NegD, int NegB)? expected = null;
    foreach (var first in Choices(oraclePeople[0]))
        foreach (var second in Choices(oraclePeople[1]))
        {
            if (first.Intersect(second).Any(s => Week.Capacity(s) == 1)) continue;
            var sets = new[] { first, second };
            var h = 0; var days = 0; var runs = 0;
            for (var i = 0; i < 2; i++)
                for (var d = 0; d < 5; d++)
                {
                    var active = false; var previous = false;
                    for (var p = 0; p < 4; p++)
                    {
                        var s = d * 4 + p; var current = sets[i].Contains(s);
                        if (current && oraclePeople[i].Preferences[s] == 2) h++;
                        if (current && !previous) runs++;
                        active |= current; previous = current;
                    }
                    if (active) days++;
                }
            var score = (h, -days, -runs);
            if (expected is null || score.CompareTo(expected.Value) > 0) expected = score;
        }
    var result = Scheduler.Solve(oraclePeople, TimeSpan.FromSeconds(5));
    var metrics = ScheduleRules.Evaluate(oraclePeople, result.Schedule);
    Check(expected is not null && !result.Draft && result.ProvenOptimal &&
        (metrics.Preferred, -metrics.Days, -metrics.Blocks) == expected.Value, "exhaustive oracle case " + caseIndex);
}
using (var cts = new CancellationTokenSource())
{
    cts.Cancel();
    try { Scheduler.Solve(allFree, TimeSpan.FromSeconds(1), cts.Token); throw new Exception("Cancellation failed."); }
    catch (OperationCanceledException) { Check(true, "cancellation"); }
}
foreach (var name in names.Take(5))
{
    var person = new Person(name, 4, Enumerable.Range(0, 20).Select(s => (byte)(s % 3)));
    Console.WriteLine("VECTOR:" + JsonSerializer.Serialize(new { person.Name, person.Target, Slots = person.Preferences, Code = SubmissionCodec.Encode(person) }));
}
Console.WriteLine($"TOTAL_PASS={passed}");
