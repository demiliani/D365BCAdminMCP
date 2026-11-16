namespace D365BCAdminMCP.Tests;

public class PteValidationTests
{
    [Theory]
    [InlineData("immediate", "Immediate")]
    [InlineData("UPDATEWINDOW", "UpdateWindow")]
    [InlineData(" NextMinorUpdate ", "NextMinorUpdate")]
    [InlineData("nextmajorupdate", "NextMajorUpdate")]
    public void BuildPteInstallOptions_NormalizesDeploymentSchedule(string input, string expected)
    {
        var options = D365BCAdminService.BuildPteInstallOptions(input, "forcesync", true, true, " it-IT ", out var error);

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(expected, options!.DeploymentSchedule);
        Assert.Equal("ForceSync", options.SyncMode);
        Assert.Equal("it-IT", options.LanguageId);
    }

    [Theory]
    [InlineData("Tomorrow", "Add", true, "deploymentSchedule")]
    [InlineData("Immediate", "Clean", true, "syncMode")]
    [InlineData("Immediate", "Add", false, "acceptIsvEula")]
    public void BuildPteInstallOptions_RejectsInvalidInput(string schedule, string syncMode, bool acceptIsvEula, string expectedInError)
    {
        var options = D365BCAdminService.BuildPteInstallOptions(schedule, syncMode, true, acceptIsvEula, null, out var error);

        Assert.Null(options);
        Assert.Contains(expectedInError, error);
    }

    [Fact]
    public void ValidatePteFile_RejectsMissingAndNonAppFiles()
    {
        using var folder = new TempFolder();
        var text = folder.File("notes.txt");
        File.WriteAllText(text, "x");

        Assert.StartsWith("File not found", D365BCAdminService.ValidatePteFile(folder.File("missing.app")));
        Assert.StartsWith("Not an .app file", D365BCAdminService.ValidatePteFile(text));
    }

    [Fact]
    public void ValidatePteFile_RejectsFilesOverTheApiLimit()
    {
        using var folder = new TempFolder();
        var big = folder.File("Big.app");
        using (var stream = File.Create(big))
        {
            stream.SetLength(D365BCAdminService.MaxPteFileSizeBytes + 1);
        }

        Assert.Contains("50 MB", D365BCAdminService.ValidatePteFile(big));
    }

    [Fact]
    public void PlanPteFolderInstall_OrdersByDependencies()
    {
        using var folder = new TempFolder();
        var baseId = Guid.NewGuid();
        var topId = Guid.NewGuid();
        var top = AppPackageBuilder.WriteAppJsonPackage(folder.File("A_Top.app"), topId, "Top", "1.0.0.0", new DependencySpec(baseId, "Base", "1.0.0.0"));
        var bottom = AppPackageBuilder.WriteManifestPackage(folder.File("Z_Base.app"), baseId, "Base", "1.0.0.0");

        var (ordered, errors) = D365BCAdminService.PlanPteFolderInstall(new[] { top, bottom });

        Assert.Empty(errors);
        Assert.Equal(new[] { baseId, topId }, ordered.Select(a => a.Id));
    }

    [Fact]
    public void PlanPteFolderInstall_ReportsDuplicatesAndCorruptFilesWithoutPlanning()
    {
        using var folder = new TempFolder();
        var id = Guid.NewGuid();
        var first = AppPackageBuilder.WriteAppJsonPackage(folder.File("App_1.0.app"), id, "App", "1.0.0.0");
        var second = AppPackageBuilder.WriteAppJsonPackage(folder.File("App_2.0.app"), id, "App", "2.0.0.0");
        var corrupt = folder.File("Corrupt.app");
        File.WriteAllText(corrupt, "this is not a package");

        var (ordered, errors) = D365BCAdminService.PlanPteFolderInstall(new[] { first, second, corrupt });

        Assert.Empty(ordered);
        Assert.Contains(errors, e => e.Contains("Corrupt.app"));
        Assert.Contains(errors, e => e.Contains("more than one file"));
    }

    [Fact]
    public void PlanPteFolderInstall_ReportsCircularDependencies()
    {
        using var folder = new TempFolder();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = AppPackageBuilder.WriteAppJsonPackage(folder.File("First.app"), firstId, "First", "1.0.0.0", new DependencySpec(secondId, "Second", "1.0.0.0"));
        var second = AppPackageBuilder.WriteAppJsonPackage(folder.File("Second.app"), secondId, "Second", "1.0.0.0", new DependencySpec(firstId, "First", "1.0.0.0"));

        var (ordered, errors) = D365BCAdminService.PlanPteFolderInstall(new[] { first, second });

        Assert.Empty(ordered);
        Assert.Contains("Circular dependency", Assert.Single(errors));
    }
}
