using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WindowsRepairCenter.Core;

namespace WindowsRepairCenter.Tests;

[TestClass]
public sealed class OperationTests
{
    [TestMethod]
    public void Catalog_contains_the_expected_operation_groups()
    {
        var catalog = OperationCatalog.Create();
        Assert.IsTrue(catalog.Count >= 15);
        Assert.IsTrue(catalog.Any(x => x.Category == OperationCategory.Diagnostic));
        Assert.IsTrue(catalog.Any(x => x.Category == OperationCategory.SafeRepair));
        Assert.IsTrue(catalog.Any(x => x.Category == OperationCategory.ExpertRepair));
    }

    [TestMethod]
    public void BcdBoot_rejects_missing_required_inputs()
    {
        var operation = OperationCatalog.Create().Single(x => x.Id == "bcdboot");
        Assert.IsNotNull(operation.Validate!(new OperationInputs()));
    }

    [TestMethod]
    public void Runner_uses_direct_argument_execution()
    {
        var command = ProcessRunner.CreateStartInfo(new CommandStep("tool.exe", new[] { "argument with spaces", "%SystemDrive%" }));
        Assert.AreEqual("argument with spaces", command.ArgumentList[0]);
        Assert.IsFalse(command.UseShellExecute);
        Assert.IsTrue(command.RedirectStandardOutput && command.RedirectStandardError);
    }

    [TestMethod]
    public async Task Session_store_writes_all_report_formats()
    {
        var root = Path.Combine(Path.GetTempPath(), "WrcTest_" + Guid.NewGuid().ToString("N"));
        var store = new SessionStore(root); store.Start();
        await store.SaveAsync(new[] { new OperationResult("test", "Test", DateTimeOffset.Now, DateTimeOffset.Now, true, false, Array.Empty<StepResult>()) });
        Assert.IsTrue(File.Exists(Path.Combine(store.SessionDirectory, "results.json")));
        Assert.IsTrue(File.Exists(Path.Combine(store.SessionDirectory, "report.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(store.SessionDirectory, "report.html")));
        Directory.Delete(root, true);
    }
}
