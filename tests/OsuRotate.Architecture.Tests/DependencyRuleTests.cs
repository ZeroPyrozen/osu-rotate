using System.Reflection;
using System.Xml.Linq;
using OsuRotate.Application;
using OsuRotate.Domain;
using OsuRotate.Infrastructure;

namespace OsuRotate.Architecture.Tests;

// Clean Architecture's dependency rule: references may only point inward.
// Declared references are read from the project files because the compiler drops unused ones
// from the assembly. Compiled references catch third-party packages that reach the core.
public class DependencyRuleTests
{
    [Theory]
    [InlineData("OsuRotate.Domain", "")]
    [InlineData("OsuRotate.Application", "OsuRotate.Domain")]
    [InlineData("OsuRotate.Infrastructure", "OsuRotate.Domain,OsuRotate.Application")]
    [InlineData("OsuRotate.Web", "OsuRotate.Domain,OsuRotate.Application,OsuRotate.Infrastructure")]
    public void Projects_only_reference_inner_layers(string project, string allowed)
    {
        var allowedProjects = allowed.Split(',', StringSplitOptions.RemoveEmptyEntries);

        var outward = ProjectReferences(project).Except(allowedProjects);

        Assert.Empty(outward);
    }

    [Fact]
    public void Domain_uses_only_the_base_class_library()
    {
        var names = ReferencedAssemblies(typeof(LobbyStatus).Assembly);

        Assert.DoesNotContain(names, name => !IsBaseClassLibrary(name));
    }

    [Fact]
    public void Application_uses_only_Domain_and_the_base_class_library()
    {
        var names = ReferencedAssemblies(typeof(ILobbyStatusReader).Assembly);

        Assert.DoesNotContain(names, name => !IsBaseClassLibrary(name) && name != "OsuRotate.Domain");
    }

    [Fact]
    public void Infrastructure_does_not_use_the_web_layer()
    {
        var names = ReferencedAssemblies(typeof(ServiceCollectionExtensions).Assembly);

        Assert.DoesNotContain(names, name => name == "OsuRotate.Web" || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    private static bool IsBaseClassLibrary(string name) =>
        name is "netstandard" or "mscorlib" or "System" || name.StartsWith("System.", StringComparison.Ordinal);

    private static IEnumerable<string> ReferencedAssemblies(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(reference => reference.Name!);

    private static IEnumerable<string> ProjectReferences(string project)
    {
        var path = Path.Combine(RepositoryRoot(), "src", project, $"{project}.csproj");

        return XDocument.Load(path).Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value.Replace('\\', '/')));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OsuRotate.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("OsuRotate.slnx was not found above the test output folder.");
    }
}
