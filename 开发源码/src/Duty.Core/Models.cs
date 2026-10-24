using System.Text;

namespace Duty.Core;

public static class Week
{
    public const int SlotCount = 20;
    public const int SpecialSlot = 7;
    public static readonly string[] Days = ["周一", "周二", "周三", "周四", "周五"];
    public static readonly string[] Periods = ["第一节", "第二节", "第三节", "第四节"];
    public static readonly string[] Preferences = ["不可安排", "正常可排", "优先安排"];
    public static int Capacity(int slot) => slot == SpecialSlot ? 2 : 1;
    public static string Label(int slot) => $"{Days[slot / 4]}{Periods[slot % 4]}";
}

public sealed class Person
{
    private readonly byte[] _preferences;
    public string Name { get; }
    public int Target { get; }
    public IReadOnlyList<byte> Preferences => Array.AsReadOnly(_preferences);
    public bool Special => _preferences[Week.SpecialSlot] > 0;
    public int Available => _preferences.Count(p => p > 0);
    public int Preferred => _preferences.Count(p => p == 2);
    public int AvailableMornings => Enumerable.Range(0, 5).Count(d => _preferences[d * 4] > 0);

    public Person(string name, int target, IEnumerable<byte> preferences)
    {
        Name = name.Trim();
        if (Name.Length == 0 || Name.EnumerateRunes().Count() > 32)
            throw new ArgumentException("请输入姓名，最多32个字符。");
        if (Name.Any(char.IsControl) || Name.Any(c => c is '\u2028' or '\u2029'))
            throw new ArgumentException("姓名不能包含换行或控制字符。");
        _ = new UTF8Encoding(false, true).GetBytes(Name);
        if (target is not (3 or 4)) throw new ArgumentException("每周班量只能选择3节或4节。");
        Target = target;
        _preferences = preferences.ToArray();
        if (_preferences.Length != Week.SlotCount || _preferences.Any(p => p > 2))
            throw new ArgumentException("时间选择必须包含20格，且只能使用三种有效状态。");
    }

    public byte[] CopyPreferences() => (byte[])_preferences.Clone();
}

public sealed class Schedule
{
    public string?[][] Cells { get; }
    public Schedule() => Cells = Enumerable.Range(0, 20).Select(s => new string?[Week.Capacity(s)]).ToArray();
    private Schedule(string?[][] cells) => Cells = cells;
    public Schedule Copy() => new(Cells.Select(c => (string?[])c.Clone()).ToArray());
    public int Assigned => Cells.Sum(c => c.Count(n => n is not null));
}

public sealed record Issue(string Message, string? Name = null, int? Slot = null, bool Hard = false);
public sealed record PersonSummary(string Name, int Target, int Actual, int Mornings, int Preferred, int Days, int Blocks)
{
    public int Missing => Target - Actual;
    public string Load => $"{Actual} / {Target}";
    public string MorningLabel => Mornings == 0 ? "缺早一" : $"{Mornings}节";
    public string State => Missing > 0 ? $"还差{Missing}节" : Mornings == 0 ? "缺早一" : "已满足";
}
public sealed record Evaluation(IReadOnlyList<PersonSummary> People, IReadOnlyList<Issue> Issues, int Assigned)
{
    public int Gaps => 21 - Assigned;
    public int Preferred => People.Sum(p => p.Preferred);
    public int Days => People.Sum(p => p.Days);
    public int Blocks => People.Sum(p => p.Blocks);
    public bool PersonalSatisfied => People.All(p => p.Missing == 0 && p.Mornings > 0)
        && !Issues.Any(i => i.Hard);
}

public static class ScheduleRules
{
    public static Evaluation Evaluate(IReadOnlyList<Person> people, Schedule schedule)
    {
        var byName = people.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var issues = new List<Issue>();
        for (var s = 0; s < 20; s++)
        {
            var names = schedule.Cells[s].Where(n => n is not null).Cast<string>().ToArray();
            if (names.Length > Week.Capacity(s) || names.Distinct().Count() != names.Length)
                issues.Add(new($"{Week.Label(s)}人数超额或重复。", Slot: s, Hard: true));
            foreach (var name in names)
            {
                if (!byName.TryGetValue(name, out var person))
                    issues.Add(new($"无法识别值班人：{name}。", name, s, true));
                else if (person.Preferences[s] == 0)
                    issues.Add(new($"{name}在{Week.Label(s)}不能安排。", name, s, true));
            }
            var missing = Week.Capacity(s) - names.Length;
            if (missing > 0) issues.Add(new($"{Week.Label(s)}缺{missing}人。", Slot: s));
        }
        var summaries = new List<PersonSummary>();
        foreach (var person in people)
        {
            var slots = Enumerable.Range(0, 20).Where(s => schedule.Cells[s].Contains(person.Name)).ToArray();
            var mornings = slots.Count(s => s % 4 == 0);
            var blocks = slots.Count(s => s % 4 == 0 || !slots.Contains(s - 1));
            var summary = new PersonSummary(person.Name, person.Target, slots.Length, mornings,
                slots.Count(s => person.Preferences[s] == 2), slots.Select(s => s / 4).Distinct().Count(), blocks);
            summaries.Add(summary);
            if (slots.Length > person.Target)
                issues.Add(new($"{person.Name}已排{slots.Length}节，超过所选{person.Target}节。", person.Name, Hard: true));
            if (summary.Missing > 0)
            {
                var reason = person.Available < person.Target
                    ? $"，仅有{person.Available}个可排时段" : "，请核对可排时间与岗位竞争";
                issues.Add(new($"{person.Name}目标{person.Target}节，实际{slots.Length}节，还差{summary.Missing}节{reason}。", person.Name));
            }
            if (mornings == 0)
            {
                var reason = person.AvailableMornings == 0 ? "没有可排的早一时段" : "当前未排到早一";
                issues.Add(new($"{person.Name}缺早一：{reason}。", person.Name));
            }
        }
        return new(summaries, issues, schedule.Assigned);
    }

    public static bool TryPlace(IReadOnlyList<Person> people, Schedule current, int slot, int position,
        string? name, out Schedule next, out string error)
    {
        next = current.Copy();
        error = "";
        if (slot is < 0 or >= 20 || position < 0 || position >= Week.Capacity(slot))
        { error = "岗位位置无效。"; return false; }
        next.Cells[slot][position] = name;
        var hard = Evaluate(people, next).Issues.FirstOrDefault(i => i.Hard);
        if (hard is null) return true;
        error = hard.Message;
        next = current;
        return false;
    }

    public static bool TrySwap(IReadOnlyList<Person> people, Schedule current,
        int fromSlot, int fromPosition, int toSlot, int toPosition, out Schedule next, out string error)
    {
        next = current.Copy();
        error = "";
        if (fromSlot is < 0 or >= 20 || toSlot is < 0 or >= 20 ||
            fromPosition < 0 || fromPosition >= Week.Capacity(fromSlot) ||
            toPosition < 0 || toPosition >= Week.Capacity(toSlot))
        { error = "岗位位置无效。"; return false; }
        (next.Cells[fromSlot][fromPosition], next.Cells[toSlot][toPosition]) =
            (next.Cells[toSlot][toPosition], next.Cells[fromSlot][fromPosition]);
        var hard = Evaluate(people, next).Issues.FirstOrDefault(i => i.Hard);
        if (hard is null) return true;
        error = hard.Message;
        next = current;
        return false;
    }
}

public sealed class UndoHistory<T>(int limit = 100)
{
    private readonly List<T> _undo = [];
    private readonly List<T> _redo = [];
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public void Push(T state)
    {
        _undo.Add(state);
        if (_undo.Count > limit) _undo.RemoveAt(0);
        _redo.Clear();
    }
    public T Undo(T current)
    {
        if (!CanUndo) return current;
        _redo.Add(current);
        var previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        return previous;
    }
    public T Redo(T current)
    {
        if (!CanRedo) return current;
        _undo.Add(current);
        var next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        return next;
    }
}
