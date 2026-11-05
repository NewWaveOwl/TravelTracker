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
     * Checks that every Domain project has no project or package references.
     * @param project Domain project folder name under src.
     */
    [Theory]
    [InlineData("TravelTracker.Domain")]
    [InlineData("TravelTracker.Attractions.Domain")]
    public void Domain_HasNoReferences(string project)
    {
        var document = LoadProject(project);
        Assert.Empty(document.Descendants("ProjectReference"));
        Assert.Empty(document.Descendants("PackageReference"));
    }

    /**
     * Checks that every Data project uses the plain SDK and only references its own Domain.
     * @param project Data project folder name under src.
     * @param domain The only project it may reference.
     */
    [Theory]
    [InlineData("TravelTracker.Data", "TravelTracker.Domain")]
    [InlineData("TravelTracker.Attractions.Data", "TravelTracker.Attractions.Domain")]
    public void Data_IsNotWebAndOnlyReferencesDomain(string project, string domain)
    {
        var document = LoadProject(project);
        Assert.DoesNotContain("Web", document.Root!.Attribute("Sdk")!.Value);
        var references = document.Descendants("ProjectReference").Select(reference => (string)reference.Attribute("Include")!).ToList();
        Assert.Single(references);
        Assert.EndsWith(domain + ".csproj", references[0]);
    }

    /**
     * Checks that the frontend talks to services over HTTP only, with no project references at all.
     */
    [Fact]
    public void Frontend_HasNoProjectReferences()
    {
        Assert.Empty(LoadProject("TravelTracker.Frontend").Descendants("ProjectReference"));
    }
}
