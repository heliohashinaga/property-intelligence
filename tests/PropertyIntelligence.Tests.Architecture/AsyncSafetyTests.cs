using Shouldly;

namespace PropertyIntelligence.Tests.Architecture;

/// <summary>
/// Guards against async anti-patterns that cause deadlocks or thread-pool starvation
/// in an ASP.NET Core / Minimal API context.
///
/// Rules enforced:
///  - No .Result on Task/ValueTask (blocks a thread; deadlocks under ASP.NET sync context)
///  - No .Wait() on Task (same problem)
///  - No Thread.Sleep (always wrong in async code — use Task.Delay)
///  - No blocking GetAwaiter().GetResult() chains in production code
/// </summary>
public class AsyncSafetyTests
{
    // Source files to scan (exclude obj/ and generated code)
    private static readonly string ProjectRoot =
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,                        // bin/Debug/net10.0
            "..", "..", "..",                              // → tests/Tests.Architecture/
            "..", "..",                                    // → repo root
            "src"));

    private IEnumerable<string> GetSourceFiles() =>
        Directory.EnumerateFiles(ProjectRoot, "*.cs", SearchOption.AllDirectories)
                 .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));

    [Fact(DisplayName = "No production code should call .Result on Task or ValueTask")]
    public void NoTaskDotResult_InProductionCode()
    {
        var violations = GetSourceFiles()
            .SelectMany(file =>
                File.ReadAllLines(file)
                    .Select((line, i) => (file, line, lineNo: i + 1))
                    .Where(x => System.Text.RegularExpressions.Regex.IsMatch(
                        x.line,
                        @"\bTask\b.*\.Result\b|\bValueTask\b.*\.Result\b|\.GetAwaiter\(\)\.GetResult\(\)"))
                    .Where(x => !x.line.TrimStart().StartsWith("//"))  // ignore comments
            )
            .Select(x => $"{Path.GetRelativePath(ProjectRoot, x.file)}:{x.lineNo}  →  {x.line.Trim()}")
            .ToList();

        violations.ShouldBeEmpty(
            "Blocking on Task.Result or GetAwaiter().GetResult() causes deadlocks in ASP.NET. Use 'await' instead.");
    }

    [Fact(DisplayName = "No production code should call Task.Wait()")]
    public void NoTaskDotWait_InProductionCode()
    {
        var violations = GetSourceFiles()
            .SelectMany(file =>
                File.ReadAllLines(file)
                    .Select((line, i) => (file, line, lineNo: i + 1))
                    .Where(x => System.Text.RegularExpressions.Regex.IsMatch(
                        x.line, @"\b\.Wait\(\)"))
                    .Where(x => !x.line.TrimStart().StartsWith("//"))
            )
            .Select(x => $"{Path.GetRelativePath(ProjectRoot, x.file)}:{x.lineNo}  →  {x.line.Trim()}")
            .ToList();

        violations.ShouldBeEmpty(
            "Task.Wait() blocks a thread and can deadlock under ASP.NET. Use 'await' instead.");
    }

    [Fact(DisplayName = "No production code should call Thread.Sleep")]
    public void NoThreadSleep_InProductionCode()
    {
        var violations = GetSourceFiles()
            .SelectMany(file =>
                File.ReadAllLines(file)
                    .Select((line, i) => (file, line, lineNo: i + 1))
                    .Where(x => System.Text.RegularExpressions.Regex.IsMatch(
                        x.line, @"\bThread\.Sleep\b"))
                    .Where(x => !x.line.TrimStart().StartsWith("//"))
            )
            .Select(x => $"{Path.GetRelativePath(ProjectRoot, x.file)}:{x.lineNo}  →  {x.line.Trim()}")
            .ToList();

        violations.ShouldBeEmpty(
            "Thread.Sleep blocks a thread-pool thread. Use 'await Task.Delay(...)' instead.");
    }
}
