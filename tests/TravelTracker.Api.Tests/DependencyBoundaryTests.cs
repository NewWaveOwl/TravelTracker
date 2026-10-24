using System.Xml.Linq;

namespace TravelTracker.Api.Tests;

/**
 * Guards the one-way layer dependencies: Domain depends on nothing, Data is not a web project.
 */
public sealed class DependencyBoundaryTests
{
    /**
     * Loads a source project file by walking up from the test output to the repository root.
     * @param project Project folder name under src.
     * @return The parsed project file.
     */
    private static XDocument LoadProject(string project)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "TravelTracker.Tests.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return XDocument.Load(Path.Combine(directory.FullName, "src", project, project + ".csproj"));
    }

    /**
     * Checks that Domain has no project or package references.
     */
    [Fact]
    public void Domain_HasNoReferences()
    {
        var document = LoadProject("TravelTracker.Domain");
        Assert.Empty(document.Descendants("ProjectReference"));
        Assert.Empty(document.Descendants("PackageReference"));
    }

    /**
     * Checks that Data uses the plain SDK and only references Domain.
     */
    [Fact]
    public void Data_IsNotWebAndOnlyReferencesDomain()
    {
        var document = LoadProject("TravelTracker.Data");
        Assert.DoesNotContain("Web", document.Root!.Attribute("Sdk")!.Value);
        var references = document.Descendants("ProjectReference").Select(reference => (string)reference.Attribute("Include")!).ToList();
        Assert.Single(references);
        Assert.Contains("TravelTracker.Domain", references[0]);
    }
}
