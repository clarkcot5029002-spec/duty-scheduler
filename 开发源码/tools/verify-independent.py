import base64
import hashlib
import json
import pathlib
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile
import zlib

sys.stdout.reconfigure(encoding="utf-8")
root = pathlib.Path(sys.argv[1]).resolve()
source = root / "开发源码"
tests = source / "tests" / "Duty.Tests" / "bin" / "Release" / "net10.0-windows" / "Duty.Tests.dll"
completed = subprocess.run(["dotnet", str(tests)], capture_output=True, encoding="utf-8", check=True)
vectors = [json.loads(line[7:]) for line in completed.stdout.splitlines() if line.startswith("VECTOR:")]
assert len(vectors) == 5
lengths = {}
for vector in vectors:
    compact = vector["Code"].replace("-", "")
    assert compact.startswith("PB1") and compact.isascii()
    body = compact[3:]
    raw = base64.b32decode(body + "=" * (-len(body) % 8))
    assert len(raw) == 11 + raw[0]
    assert zlib.crc32(b"PB1" + raw[:-4]) == int.from_bytes(raw[-4:], "big")
    assert raw[7:-4].decode("utf-8") == vector["Name"]
    assert (4 if raw[1] & 1 else 3) == vector["Target"]
    slots = [(raw[2 + s // 4] >> (6 - 2 * (s % 4))) & 3 for s in range(20)]
    assert slots == vector["Slots"]
    assert bool(raw[1] & 2) == (slots[7] > 0)
    lengths[vector["Name"]] = len(vector["Code"])

S = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"
template = root / "排班系统" / "排班模板.xlsx"
generated = (pathlib.Path(sys.argv[2]).resolve() if len(sys.argv) > 2 else
             max((source / "qa" / "导出验证").glob("*.xlsx"), key=lambda p: p.stat().st_mtime_ns))

def inspect_xlsx(path):
    with zipfile.ZipFile(path) as archive:
        workbook = ET.fromstring(archive.read("xl/workbook.xml"))
        assert len(workbook.find(S + "sheets")) == 1
        sheet = ET.fromstring(archive.read("xl/worksheets/sheet1.xml"))
        shared = []
        if "xl/sharedStrings.xml" in archive.namelist():
            shared = ["".join(item.itertext()) for item in ET.fromstring(archive.read("xl/sharedStrings.xml"))]
        cells = {}
        for cell in sheet.iter(S + "c"):
            address = cell.attrib["r"]
            assert address not in cells
            if cell.attrib.get("t") == "inlineStr":
                text = "".join(t.text or "" for t in cell.iter(S + "t"))
            elif cell.attrib.get("t") == "s":
                text = shared[int(cell.find(S + "v").text)]
            else:
                value = cell.find(S + "v")
                text = value.text if value is not None else ""
            cells[address] = text
        assert not list(sheet.iter(S + "f")), "Unexpected formula in static schedule"
        return workbook, sheet, cells

_, _, blank = inspect_xlsx(template)
for col in "CDEFG":
    for row in range(6, 10):
        assert blank.get(f"{col}{row}", "") == ""
workbook, worksheet, cells = inspect_xlsx(generated)
assert worksheet.find(S + "pageSetup").attrib["orientation"] == "landscape"
assert worksheet.find(S + "pageSetup").attrib["fitToWidth"] == "1"
positions = [cells[f"{col}{row}"].split("\n") for col in "CDEFG" for row in range(6, 10)]
assert len(positions[7]) == 2
assert sum(len(value) for value in positions) == 21
assigned = [name for values in positions for name in values if name != "无"]
assert len(assigned) == 20
assert len(set(assigned)) == 5
assert all(assigned.count(name) == 4 for name in set(assigned))
assert len({cells[f"{col}6"] for col in "CDEFG"}) == 5
assert any("周二第四节缺1人" in text for text in cells.values())
assert any(node.attrib.get("name") == "_xlnm.Print_Area" for node in workbook.iter(S + "definedName"))
print(json.dumps({
    "core_tests": completed.stdout.split("TOTAL_PASS=")[-1].strip(),
    "independent_base32_crc_vectors": len(vectors),
    "code_lengths": lengths,
    "excel_assigned": len(assigned),
    "excel_gaps": 21 - len(assigned),
    "template_sha256": hashlib.sha256(template.read_bytes()).hexdigest(),
    "generated_sha256": hashlib.sha256(generated.read_bytes()).hexdigest(),
    "generated_file": str(generated),
    "result": "PASS",
}, ensure_ascii=False, indent=2))
