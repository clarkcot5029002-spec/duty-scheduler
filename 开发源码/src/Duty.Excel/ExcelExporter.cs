using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Duty.Core;

namespace Duty.Excel;

public static class ExcelExporter
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static string Export(string template, string directory, IReadOnlyList<Person> people, Schedule schedule,
        bool collectionConfirmed, string computationNote = "", DateTime? systemTime = null)
    {
        if (!File.Exists(template)) throw new FileNotFoundException("找不到排班模板.xlsx，请将完整程序文件夹一起复制。", template);
        var evaluation = ScheduleRules.Evaluate(people, schedule);
        if (evaluation.Issues.Any(i => i.Hard)) throw new InvalidOperationException("当前结果包含禁排、重复或超额安排，不能导出。");
        if (people.Count == 0) throw new InvalidOperationException("没有人员记录，不能生成排班表。");
        var now = systemTime ?? DateTime.Now;
        Directory.CreateDirectory(directory);
        using var input = ZipFile.OpenRead(template);
        var worksheet = Load(input, "xl/worksheets/sheet1.xml");
        var workbook = Load(input, "xl/workbook.xml");
        if (workbook.Root?.Element(S + "sheets")?.Elements(S + "sheet").Count() != 1)
            throw new InvalidDataException("排班模板必须只包含一张工作表。");
        var root = worksheet.Root!;
        var data = root.Element(S + "sheetData") ?? throw new InvalidDataException("模板缺少表格数据。");
        var mergeCells = root.Element(S + "mergeCells");
        if (mergeCells is null) { mergeCells = new XElement(S + "mergeCells"); root.Add(mergeCells); }
        var noteStyle = FindCell(data, "A13")?.Attribute("s")?.Value ?? "0";
        SetCell(data, "A2", "值班表");
        SetCell(data, "A3", $"生成时间：{now:yyyy-MM-dd HH:mm:ss}    已排{evaluation.Assigned}/21人次    缺{evaluation.Gaps}人次");
        for (var d = 0; d < 5; d++)
            for (var p = 0; p < 4; p++)
            {
                var s = d * 4 + p;
                var text = string.Join("\n", schedule.Cells[s].Select(name => name ?? "无"));
                SetCell(data, $"{(char)('C' + d)}{6 + p}", text);
            }
        var warnings = new List<string>();
        if (!collectionConfirmed) warnings.Add("提醒：管理员尚未确认本次记录已经收齐，结果仅基于当前输入。");
        if (!evaluation.PersonalSatisfied) warnings.Add("草案：存在个人班量或早一要求未满足，请核对后使用。");
        if (!string.IsNullOrWhiteSpace(computationNote)) warnings.Add(computationNote);
        warnings.AddRange(evaluation.Issues.Select(i => i.Message));
        if (warnings.Count == 0) warnings.Add("个人要求与全部岗位均已满足。");
        SetCell(data, "A11", evaluation.PersonalSatisfied ? "个人要求已满足；岗位缺员情况见下方。" : "草案提醒：存在未满足项。");
        SetCell(data, "A12", "排班检查");
        foreach (var row in data.Elements(S + "row").Where(r => (int?)r.Attribute("r") >= 13).ToList()) row.Remove();
        foreach (var merge in mergeCells.Elements(S + "mergeCell").Where(m => RowOf(((string?)m.Attribute("ref") ?? "").Split(':')[0]) >= 13).ToList())
            merge.Remove();
        var lastRow = 12;
        foreach (var warning in warnings)
        {
            lastRow++;
            var row = new XElement(S + "row", new XAttribute("r", lastRow),
                new XAttribute("ht", Math.Max(27, Math.Ceiling(warning.Length / 48.0) * 19 + 8).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("customHeight", "1"));
            data.Add(row);
            SetCell(data, "A" + lastRow, warning, noteStyle);
            mergeCells.Add(new XElement(S + "mergeCell", new XAttribute("ref", $"A{lastRow}:G{lastRow}")));
        }
        mergeCells.SetAttributeValue("count", mergeCells.Elements(S + "mergeCell").Count());
        var dimension = root.Element(S + "dimension");
        dimension?.SetAttributeValue("ref", $"A1:G{lastRow}");
        AddPrintSettings(root);
        var sheetName = workbook.Root!.Element(S + "sheets")?.Elements(S + "sheet").First().Attribute("name")?.Value ?? "值班表";
        var names = workbook.Root.Element(S + "definedNames");
        if (names is null)
        {
            names = new XElement(S + "definedNames");
            var calc = workbook.Root.Element(S + "calcPr");
            if (calc is null) workbook.Root.Add(names); else calc.AddBeforeSelf(names);
        }
        names.Elements(S + "definedName").Where(n => (string?)n.Attribute("name") == "_xlnm.Print_Area").Remove();
        names.Add(new XElement(S + "definedName", new XAttribute("name", "_xlnm.Print_Area"), new XAttribute("localSheetId", 0),
            $"'{sheetName.Replace("'", "''")}'!$A$1:$G${lastRow}"));
        var stem = now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_排班表";
        for (var suffix = 0; suffix < 1000; suffix++)
        {
            var path = Path.Combine(directory, stem + (suffix == 0 ? "" : $"_{suffix + 1:00}") + ".xlsx");
            FileStream output;
            try { output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (File.Exists(path)) { continue; }
            try
            {
                using (output)
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
                    foreach (var entry in input.Entries)
                    {
                        var copy = zip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                        using var stream = copy.Open();
                        if (entry.FullName == "xl/worksheets/sheet1.xml") worksheet.Save(stream);
                        else if (entry.FullName == "xl/workbook.xml") workbook.Save(stream);
                        else { using var source = entry.Open(); source.CopyTo(stream); }
                    }
                return path;
            }
            catch
            {
                output.Dispose();
                if (File.Exists(path)) File.Delete(path);
                throw;
            }
        }
        throw new IOException("相同时间命名的文件过多，请稍后重试。");
    }

    private static int RowOf(string address) => int.TryParse(new string(address.Where(char.IsDigit).ToArray()), out var row) ? row : 0;
    private static XDocument Load(ZipArchive zip, string name)
    {
        using var stream = (zip.GetEntry(name) ?? throw new InvalidDataException("模板缺少：" + name)).Open();
        return XDocument.Load(stream);
    }
    private static XElement? FindCell(XElement data, string reference) => data.Elements(S + "row")
        .SelectMany(r => r.Elements(S + "c")).FirstOrDefault(c => (string?)c.Attribute("r") == reference);
    private static void SetCell(XElement data, string reference, string text, string? style = null)
    {
        var rowNumber = RowOf(reference);
        var row = data.Elements(S + "row").FirstOrDefault(r => (int?)r.Attribute("r") == rowNumber);
        if (row is null) { row = new XElement(S + "row", new XAttribute("r", rowNumber)); data.Add(row); }
        var cell = row.Elements(S + "c").FirstOrDefault(c => (string?)c.Attribute("r") == reference);
        if (cell is null) { cell = new XElement(S + "c", new XAttribute("r", reference)); row.Add(cell); }
        if (style is not null) cell.SetAttributeValue("s", style);
        cell.SetAttributeValue("t", "inlineStr");
        cell.RemoveNodes();
        cell.Add(new XElement(S + "is", new XElement(S + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
    }
    private static void AddPrintSettings(XElement root)
    {
        var properties = root.Element(S + "sheetPr");
        if (properties is null) { properties = new XElement(S + "sheetPr"); root.AddFirst(properties); }
        var fit = properties.Element(S + "pageSetUpPr");
        if (fit is null) { fit = new XElement(S + "pageSetUpPr"); properties.Add(fit); }
        fit.SetAttributeValue("fitToPage", "1");
        root.Elements(S + "pageMargins").Remove();
        root.Elements(S + "pageSetup").Remove();
        root.Add(new XElement(S + "pageMargins", new XAttribute("left", "0.3"), new XAttribute("right", "0.3"),
            new XAttribute("top", "0.35"), new XAttribute("bottom", "0.35"), new XAttribute("header", "0.15"), new XAttribute("footer", "0.15")));
        root.Add(new XElement(S + "pageSetup", new XAttribute("paperSize", "9"), new XAttribute("orientation", "landscape"),
            new XAttribute("fitToWidth", "1"), new XAttribute("fitToHeight", "0")));
    }
}
