using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

if (args.Length == 0) throw new ArgumentException("Supply a published application directory.");
var root = Path.GetFullPath(args[0]);
var vcPattern = new Regex(@"^(msvcp|vcruntime|concrt|vcomp)14\d.*\.dll$", RegexOptions.IgnoreCase);
var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
for (var pass = 0; pass < 6; pass++)
{
    var added = false;
    foreach (var file in Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories).ToArray())
    {
        foreach (var import in Imports(file).Where(name => vcPattern.IsMatch(name)))
        {
            var destination = Path.Combine(root, import);
            if (File.Exists(destination)) continue;
            var source = Path.Combine(Environment.SystemDirectory, import);
            if (!File.Exists(source)) throw new FileNotFoundException("Required app-local C++ runtime is missing.", source);
            File.Copy(source, destination, false);
            copied.Add(import); added = true;
        }
    }
    if (!added) break;
}
var required = Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories)
    .SelectMany(Imports).Where(name => vcPattern.IsMatch(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
foreach (var import in required)
    if (!File.Exists(Path.Combine(root, import))) throw new Exception("Unresolved portable dependency: " + import);
Console.WriteLine("APP_LOCAL_VC=" + string.Join(",", required.Order()));
Console.WriteLine("COPIED=" + string.Join(",", copied.Order()));
Console.WriteLine("DEPENDENCY_CHECK=PASS");

static IEnumerable<string> Imports(string path)
{
    using var stream = File.OpenRead(path);
    PEReader pe;
    try { pe = new PEReader(stream); }
    catch (BadImageFormatException) { yield break; }
    using (pe)
    {
        var directory = pe.PEHeaders.PEHeader?.ImportTableDirectory;
        if (directory is null || directory.Value.RelativeVirtualAddress == 0) yield break;
        var reader = pe.GetSectionData(directory.Value.RelativeVirtualAddress).GetReader();
        while (reader.RemainingBytes >= 20)
        {
            var lookup = reader.ReadInt32();
            var stamp = reader.ReadInt32();
            var forward = reader.ReadInt32();
            var name = reader.ReadInt32();
            var table = reader.ReadInt32();
            if (lookup == 0 && stamp == 0 && forward == 0 && name == 0 && table == 0) break;
            if (name <= 0) continue;
            var stringReader = pe.GetSectionData(name).GetReader();
            var bytes = new List<byte>();
            while (stringReader.RemainingBytes > 0)
            {
                var value = stringReader.ReadByte();
                if (value == 0) break;
                bytes.Add(value);
            }
            yield return Encoding.ASCII.GetString(bytes.ToArray());
        }
    }
}
