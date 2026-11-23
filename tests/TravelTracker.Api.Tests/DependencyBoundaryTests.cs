using System.Text.Json;
using System.Xml.Linq;

namespace TravelTracker.Api.Tests;

/**
 * Guards the one-way layer dependencies: Domain depends on nothing, Data is not a web project.
 * Direct references come from the project file; transitive ones come from the restored graph in obj/project.assets.json.
 */
public sealed class DependencyBoundaryTests
{
    /**
     * Walks up from the test output to the repository root, which holds the test solution.
     * @return The repository root directory.
     */
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "TravelTracker.Tests.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }

    /**
     * Loads a source project file by walking up from the test output to the repository root.
     * @param project Project folder name under src.
     * @return The parsed project file.
     */
    private static XDocument LoadProject(string project) =>
        XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", project, project + ".csproj"));

    /**
     * Lists every project the restored graph reaches from a source project, directly or transitively.
     * @param project Project folder name under src.
     * @return Names of the referenced projects, without versions.
     */
    private static string[] ReachableProjectNames(string project)
    {
        var path = Path.Combine(FindRepositoryRoot(), "src", project, "obj", "project.assets.json");
        Assert.True(File.Exists(path), $"restore and build first: {path} is missing.");
        using var assets = JsonDocument.Parse(File.ReadAllText(path));
        return assets.RootElement.GetProperty("libraries").EnumerateObject()
            .Where(library => library.Value.GetProperty("type").GetString() == "project")
            .Select(library => library.Name.Split('/')[0])
            .ToArray();
    }

    /**
     * Checks that every Domain project has no project or package references, direct or transitive.
     * @param project Domain project folder name under src.
     */
    [Theory]
    [InlineData("TravelTracker.Domain")]
    [InlineData("TravelTracker.Attractions.Domain")]
    [InlineData("TravelTracker.Accommodations.Domain")]
    [InlineData("TravelTracker.Users.Domain")]
    [InlineData("TravelTracker.Transport.Domain")]
    [InlineData("TravelTracker.Trips.Domain")]
    [InlineData("TravelTracker.Settings.Domain")]
    public void Domain_HasNoReferences(string project)
    {
        var document = LoadProject(project);
        Assert.Empty(document.Descendants("ProjectReference"));
        Assert.Empty(document.Descendants("PackageReference"));
        Assert.Empty(ReachableProjectNames(project));
    }

    /**
     * Checks that every Data project uses the plain SDK and reaches only its own Domain, directly or transitively.
     * @param project Data project folder name under src.
     * @param domain The only project it may reference.
     */
    [Theory]
    [InlineData("TravelTracker.Data", "TravelTracker.Domain")]
    [InlineData("TravelTracker.Attractions.Data", "TravelTracker.Attractions.Domain")]
    [InlineData("TravelTracker.Accommodations.Data", "TravelTracker.Accommodations.Domain")]
    [InlineData("TravelTracker.Users.Data", "TravelTracker.Users.Domain")]
    [InlineData("TravelTracker.Transport.Data", "TravelTracker.Transport.Domain")]
    [InlineData("TravelTracker.Trips.Data", "TravelTracker.Trips.Domain")]
    [InlineData("TravelTracker.Settings.Data", "TravelTracker.Settings.Domain")]
    public void Data_IsNotWebAndOnlyReachesDomain(string project, string domain)
    {
        var document = LoadProject(project);
        Assert.DoesNotContain("Web", document.Root!.Attribute("Sdk")!.Value);
        var references = document.Descendants("ProjectReference").Select(reference => (string)reference.Attribute("Include")!).ToList();
        Assert.Single(references);
        Assert.EndsWith(domain + ".csproj", references[0]);
        Assert.Equal(new[] { domain }, ReachableProjectNames(project));
    }

    /**
     * Checks that the frontend talks to services over HTTP only, with no project references at all, direct or transitive.
     */
    [Fact]
    public void Frontend_HasNoProjectReferences()
    {
        Assert.Empty(LoadProject("TravelTracker.Frontend").Descendants("ProjectReference"));
        Assert.Empty(ReachableProjectNames("TravelTracker.Frontend"));
    }
}
