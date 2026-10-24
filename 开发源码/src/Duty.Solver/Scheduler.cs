using System.Diagnostics;
using Duty.Core;
using Google.OrTools.Sat;

namespace Duty.Solver;

public sealed record StageResult(string Name, string Status, long? Value, double Bound, double Seconds);
public sealed record SolveResult(Schedule Schedule, bool Draft, bool ProvenOptimal, string Explanation,
    IReadOnlyList<StageResult> Stages);

public static class Scheduler
{
    private sealed record ModelData(CpModel Model, BoolVar[,] X, LinearExpr Preferred, LinearExpr Days,
        LinearExpr Blocks, LinearExpr Assigned, LinearExpr Mornings);

    public static SolveResult Solve(IReadOnlyList<Person> people, TimeSpan timeLimit,
        CancellationToken token = default, IProgress<string>? progress = null)
    {
        if (people.Count == 0) throw new ArgumentException("请先导入至少一位同学的填报码。");
        if (people.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != people.Count)
            throw new ArgumentException("存在同名记录，请先核对重复项。");
        token.ThrowIfCancellationRequested();
        var clock = Stopwatch.StartNew();
        var stages = new List<StageResult>();
        var impossible = people.Count > 5 || people.Any(p => p.Available < p.Target || p.AvailableMornings == 0)
            || people.Sum(p => p.Target) > 21;
        var reason = "";
        if (!impossible)
        {
            var normal = Build(people, false);
            var outcome = Optimize(normal, people, false);
            if (outcome.Result is not null) return outcome.Result;
            if (outcome.Infeasible) reason = "完整个人规则已证明不可同时满足。";
            else reason = "完整规则计算未完成，未证明无解。";
        }
        else reason = "输入条件已存在确定冲突，完整个人规则无法全部满足。";
        progress?.Report("生成带提醒的草案");
        var draft = Optimize(Build(people, true), people, true);
        return draft.Result is not null
            ? draft.Result with { Explanation = reason + draft.Result.Explanation }
            : new(new Schedule(), true, false, reason + "当前仅有空白草案，请增加计算时间或调整输入。", stages);

        (SolveResult? Result, bool Infeasible) Optimize(ModelData data, IReadOnlyList<Person> records, bool isDraft)
        {
            var objectives = new List<(string Name, LinearExpr Expression, bool Maximize)>();
            if (isDraft)
            {
                objectives.Add(("补足早一要求", data.Mornings, true));
                objectives.Add(("补足个人班量", data.Assigned, true));
            }
            objectives.AddRange([("优化优先时段", data.Preferred, true), ("减少到岗天数", data.Days, false),
                ("优化连续班段", data.Blocks, false)]);
            Schedule? best = null;
            var allOptimal = true;
            foreach (var objective in objectives)
            {
                token.ThrowIfCancellationRequested();
                var remaining = timeLimit.TotalSeconds - clock.Elapsed.TotalSeconds;
                if (remaining <= 0) { allOptimal = false; break; }
                progress?.Report(objective.Name);
                if (objective.Maximize) data.Model.Maximize(objective.Expression);
                else data.Model.Minimize(objective.Expression);
                var solver = new CpSolver
                {
                    StringParameters = $"max_time_in_seconds:{remaining.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} num_search_workers:1 random_seed:0 log_search_progress:false"
                };
                using var registration = token.Register(solver.StopSearch);
                var timer = Stopwatch.StartNew();
                var status = solver.Solve(data.Model);
                token.ThrowIfCancellationRequested();
                var solved = status is CpSolverStatus.Optimal or CpSolverStatus.Feasible;
                long? value = solved ? solver.Value(objective.Expression) : null;
                stages.Add(new(objective.Name, status.ToString(), value, solver.BestObjectiveBound, timer.Elapsed.TotalSeconds));
                if (status == CpSolverStatus.ModelInvalid)
                    throw new InvalidOperationException("排班模型校验失败：" + data.Model.Validate());
                if (!solved)
                {
                    allOptimal = false;
                    if (best is null) return (null, status == CpSolverStatus.Infeasible);
                    break;
                }
                best = Extract(data.X, solver, records);
                if (status != CpSolverStatus.Optimal) { allOptimal = false; break; }
                data.Model.Add(objective.Expression == value!.Value);
            }
            if (best is null) return (null, false);
            var explanation = isDraft ? "草案优先补足早一，再补足班量；请逐人查看未满足项。" : "个人班量与早一要求已满足。";
            explanation += allOptimal ? "各层目标已证明最优。" : "当前方案尚未证明所有层级最优。";
            return (new(best, isDraft, allOptimal, explanation, stages.ToArray()), false);
        }
    }

    private static ModelData Build(IReadOnlyList<Person> people, bool draft)
    {
        var model = new CpModel();
        var x = new BoolVar[people.Count, 20];
        var preferred = new List<IntVar>();
        var days = new List<IntVar>();
        var blocks = new List<IntVar>();
        var assigned = new List<IntVar>();
        var mornings = new List<IntVar>();
        for (var i = 0; i < people.Count; i++)
        {
            for (var s = 0; s < 20; s++)
            {
                x[i, s] = model.NewBoolVar($"x_{i}_{s}");
                assigned.Add(x[i, s]);
                if (people[i].Preferences[s] == 0) model.Add(x[i, s] == 0);
                if (people[i].Preferences[s] == 2) preferred.Add(x[i, s]);
            }
            var weekly = LinearExpr.Sum(Enumerable.Range(0, 20).Select(s => x[i, s]));
            if (draft) model.Add(weekly <= people[i].Target);
            else model.Add(weekly == people[i].Target);
            var morningVars = Enumerable.Range(0, 5).Select(d => x[i, d * 4]).ToArray();
            var hasMorning = model.NewBoolVar($"morning_{i}");
            model.AddMaxEquality(hasMorning, morningVars);
            mornings.Add(hasMorning);
            if (!draft) model.Add(hasMorning == 1);
            for (var d = 0; d < 5; d++)
            {
                var active = model.NewBoolVar($"day_{i}_{d}");
                model.AddMaxEquality(active, Enumerable.Range(0, 4).Select(p => x[i, d * 4 + p]));
                days.Add(active);
                blocks.Add(x[i, d * 4]);
                for (var p = 1; p < 4; p++)
                {
                    var current = x[i, d * 4 + p];
                    var previous = x[i, d * 4 + p - 1];
                    var start = model.NewBoolVar($"block_{i}_{d}_{p}");
                    model.Add(start >= current - previous);
                    model.Add(start <= current);
                    model.Add(start + previous <= 1);
                    blocks.Add(start);
                }
            }
        }
        for (var s = 0; s < 20; s++)
            model.Add(LinearExpr.Sum(Enumerable.Range(0, people.Count).Select(i => x[i, s])) <= Week.Capacity(s));
        return new(model, x, LinearExpr.Sum(preferred), LinearExpr.Sum(days), LinearExpr.Sum(blocks),
            LinearExpr.Sum(assigned), LinearExpr.Sum(mornings));
    }

    private static Schedule Extract(BoolVar[,] x, CpSolver solver, IReadOnlyList<Person> people)
    {
        var schedule = new Schedule();
        for (var s = 0; s < 20; s++)
        {
            var position = 0;
            for (var i = 0; i < people.Count; i++)
                if (solver.BooleanValue(x[i, s])) schedule.Cells[s][position++] = people[i].Name;
        }
        return schedule;
    }
}
