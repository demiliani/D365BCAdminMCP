namespace D365BCAdminMCP.Tests;

public class PtePackageReaderTests
{
    private static readonly Guid BaseId = Guid.NewGuid();
    private static readonly Guid MiddleId = Guid.NewGuid();
    private static readonly Guid TopId = Guid.NewGuid();

    [Fact]
    public void ReadAppInfo_ReadsAppJsonInsideNavxPackage()
    {
        using var folder = new TempFolder();
        var appSourceId = Guid.NewGuid();
        var file = AppPackageBuilder.WriteAppJsonPackage(folder.File("Top.app"), TopId, "Top", "1.2.3.4",
            new DependencySpec(appSourceId, "AppSource App", "2.0.0.0", "ISV"));

        var info = PtePackageReader.ReadAppInfo(file);

        Assert.Equal(TopId, info.Id);
        Assert.Equal("Top", info.Name);
        Assert.Equal("1.2.3.4", info.Version);
        Assert.Equal("app.json", info.MetadataSource);
        var dependency = Assert.Single(info.Dependencies);
        Assert.Equal(appSourceId, dependency.Id);
        Assert.Equal("2.0.0.0", dependency.Version);
        Assert.Equal("ISV", dependency.Publisher);
    }

    [Fact]
    public void ReadAppInfo_SupportsLegacyAppIdInAppJsonDependencies()
    {
        using var folder = new TempFolder();
        var legacyDependencyId = Guid.NewGuid();
        var file = AppPackageBuilder.WritePackage(folder.File("Legacy.app"), ("app.json",
            $$"""{"id":"{{TopId}}","name":"Legacy","version":"1.0.0.0","dependencies":[{"appId":"{{legacyDependencyId}}","name":"Old","version":"1.0.0.0"}]}"""));

        var info = PtePackageReader.ReadAppInfo(file);

        Assert.Equal(legacyDependencyId, Assert.Single(info.Dependencies).Id);
    }

    [Fact]
    public void ReadAppInfo_FallsBackToNavxManifest()
    {
        using var folder = new TempFolder();
        var file = AppPackageBuilder.WriteManifestPackage(folder.File("Middle.app"), MiddleId, "Middle", "2.0.0.0",
            new DependencySpec(BaseId, "Base", "1.5.0.0"));

        var info = PtePackageReader.ReadAppInfo(file);

        Assert.Equal(MiddleId, info.Id);
        Assert.Equal("NavxManifest.xml", info.MetadataSource);
        var dependency = Assert.Single(info.Dependencies);
        Assert.Equal(BaseId, dependency.Id);
        Assert.Equal("1.5.0.0", dependency.Version);
    }

    [Fact]
    public void ReadAppInfo_RejectsPackagesWithoutManifest()
    {
        using var folder = new TempFolder();
        var file = AppPackageBuilder.WritePackage(folder.File("Runtime.app"), ("readme.txt", "no manifest"));

        Assert.Throws<InvalidDataException>(() => PtePackageReader.ReadAppInfo(file));
    }

    [Fact]
    public void SortByDependencies_PutsDependenciesFirst()
    {
        var top = App("A_Top.app", TopId, MiddleId);
        var middle = App("M_Middle.app", MiddleId, BaseId);
        var bottom = App("Z_Base.app", BaseId);

        var order = PtePackageReader.SortByDependencies(new[] { top, middle, bottom });

        Assert.Empty(order.Cyclic);
        Assert.Equal(new[] { BaseId, MiddleId, TopId }, order.Ordered.Select(a => a.Id));
    }

    [Fact]
    public void SortByDependencies_ReportsCycles()
    {
        var first = App("First.app", TopId, MiddleId);
        var second = App("Second.app", MiddleId, TopId);
        var independent = App("Independent.app", BaseId);

        var order = PtePackageReader.SortByDependencies(new[] { first, second, independent });

        Assert.Equal(new[] { BaseId }, order.Ordered.Select(a => a.Id));
        Assert.Equal(2, order.Cyclic.Count);
    }

    private static PteAppInfo App(string file, Guid id, params Guid[] dependsOn) => new()
    {
        FilePath = file,
        Id = id,
        Name = Path.GetFileNameWithoutExtension(file),
        Dependencies = dependsOn.Select(d => new PteAppDependency { Id = d }).ToList()
    };
}
