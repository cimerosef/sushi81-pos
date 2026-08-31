using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
public sealed class DependencyBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [TestMethod]
    public void ProjectReferencesFollowTheApprovedDirection()
    {
        AssertProjectReferences("src/Sushi81.Pos.Domain/Sushi81.Pos.Domain.csproj");
        AssertProjectReferences(
            "src/Sushi81.Pos.Application/Sushi81.Pos.Application.csproj",
            "Sushi81.Pos.Domain.csproj");
        AssertProjectReferences(
            "src/Sushi81.Pos.Infrastructure/Sushi81.Pos.Infrastructure.csproj",
            "Sushi81.Pos.Application.csproj",
            "Sushi81.Pos.Domain.csproj");
        AssertProjectReferences(
            "src/Sushi81.Pos.Desktop/Sushi81.Pos.Desktop.csproj",
            "Sushi81.Pos.Application.csproj",
            "Sushi81.Pos.Domain.csproj",
            "Sushi81.Pos.Infrastructure.csproj");
    }

    [TestMethod]
    public void DomainAndApplicationDoNotReferenceForbiddenAssemblies()
    {
        AssertNoAssemblyReferences(typeof(Sushi81.Pos.Domain.Money).Assembly,
            "Sushi81.Pos.Application", "Sushi81.Pos.Infrastructure", "Sushi81.Pos.Desktop", "Microsoft.Data.Sqlite", "PresentationCore", "PresentationFramework", "WindowsBase");
        AssertNoAssemblyReferences(typeof(Sushi81.Pos.Application.Foundation.Paths.IAppPaths).Assembly,
            "Sushi81.Pos.Infrastructure", "Sushi81.Pos.Desktop", "Microsoft.Data.Sqlite", "PresentationCore", "PresentationFramework", "WindowsBase");
        AssertNoAssemblyReferences(typeof(Sushi81.Pos.Infrastructure.Paths.WindowsAppPaths).Assembly, "Sushi81.Pos.Desktop");
    }

    [TestMethod]
    public void DomainAndApplicationPublicApisDoNotExposeWpfOrSqliteTypes()
    {
        AssertNoForbiddenPublicApiTypes(typeof(Sushi81.Pos.Domain.Money).Assembly);
        AssertNoForbiddenPublicApiTypes(typeof(Sushi81.Pos.Application.Foundation.Paths.IAppPaths).Assembly);
    }

    private static void AssertProjectReferences(string relativeProjectPath, params string[] expectedFileNames)
    {
        var document = XDocument.Load(Path.Combine(RepositoryRoot, relativeProjectPath));
        var actual = document.Descendants("ProjectReference")
            .Select(reference => Path.GetFileName((string?)reference.Attribute("Include")))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var expected = expectedFileNames.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        CollectionAssert.AreEqual(expected, actual, relativeProjectPath);
    }

    private static void AssertNoAssemblyReferences(Assembly assembly, params string[] forbiddenAssemblyNames)
    {
        var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var forbidden in forbiddenAssemblyNames)
        {
            CollectionAssert.DoesNotContain(references.ToArray(), forbidden, $"{assembly.GetName().Name} must not reference {forbidden}.");
        }
    }

    private static void AssertNoForbiddenPublicApiTypes(Assembly assembly)
    {
        var forbidden = new HashSet<string>(StringComparer.Ordinal)
        {
            "Microsoft.Data.Sqlite",
            "System.Windows",
        };

        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var exposedType in GetPublicApiTypes(type))
            {
                Assert.IsFalse(IsForbidden(exposedType, forbidden),
                    $"{assembly.GetName().Name} exposes forbidden type {exposedType.FullName} through public API {type.FullName}.");
            }
        }
    }

    private static IEnumerable<Type> GetPublicApiTypes(Type type)
    {
        yield return type;

        foreach (var constructor in type.GetConstructors())
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            yield return property.PropertyType;
        }
    }

    private static bool IsForbidden(Type type, ISet<string> forbidden)
    {
        if (type.IsByRef || type.IsPointer || type.IsArray)
        {
            return IsForbidden(type.GetElementType()!, forbidden);
        }

        if (type.Namespace is not null && forbidden.Any(prefix => type.Namespace.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments().Any(argument => IsForbidden(argument, forbidden));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Sushi81.Pos.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
