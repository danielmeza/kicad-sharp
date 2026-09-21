using System.Reflection;
using System.Runtime.CompilerServices;

using KiCadSharp.Documents;

namespace KiCadSharp.Fluent.Tests;

/// <summary>
/// The fluent layer is a strict mirror of the core's <c>Add*</c> members, and this is what holds it
/// to that as the core grows: every public <c>Add*</c> on a document type has a <c>With*</c>, and
/// every <c>With*</c> mirrors an <c>Add*</c> — same name after the verb, same receiver, same
/// parameters by name and type, returning the receiver, with a callback exactly when the
/// <c>Add*</c> returns a child.
/// </summary>
/// <remarks>
/// "Document type" means a public type in <c>KiCadSharp.Documents</c> or
/// <c>KiCadSharp.Schematics</c>. <c>SpecctraNode.Add</c> is outside both on purpose: it already
/// returns the form it was called on, so it chains as it is.
/// </remarks>
public class MirrorTests
{
    private static readonly string[] DocumentNamespaces = ["KiCadSharp.Documents", "KiCadSharp.Schematics"];

    private static IReadOnlyList<MethodInfo> CoreAdds { get; } = typeof(KiCadNode).Assembly.GetExportedTypes()
        .Where(t => DocumentNamespaces.Contains(t.Namespace))
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        .Where(m => m.Name.StartsWith("Add", StringComparison.Ordinal) && !m.IsSpecialName)
        .ToArray();

    private static IReadOnlyList<MethodInfo> FluentWiths { get; } = typeof(NodeListFluentExtensions).Assembly.GetExportedTypes()
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        .ToArray();

    [Fact]
    public void EveryAddOnADocumentTypeHasAWith()
    {
        Assert.NotEmpty(CoreAdds);
        var missing = CoreAdds.Where(add => !FluentWiths.Any(with => Mirrors(with, add, allowDroppedOptionals: false))).Select(Describe).ToArray();

        Assert.True(missing.Length == 0, "No With* mirrors: " + string.Join("; ", missing));
    }

    [Fact]
    public void EveryWithMirrorsAnAdd()
    {
        var unmatched = FluentWiths.Where(with => !CoreAdds.Any(add => Mirrors(with, add, allowDroppedOptionals: true))).Select(Describe).ToArray();

        Assert.True(unmatched.Length == 0, "Mirrors no Add*: " + string.Join("; ", unmatched));
    }

    [Fact]
    public void EveryWithIsAnExtensionThatReturnsItsReceiver()
    {
        foreach (var with in FluentWiths)
        {
            Assert.True(with.IsDefined(typeof(ExtensionAttribute)), $"{Describe(with)} is not an extension method");
            Assert.Equal(with.GetParameters()[0].ParameterType, with.ReturnType);
        }
    }

    [Fact]
    public void EveryWithRefusesANullReceiverByName()
    {
        foreach (var with in FluentWiths)
        {
            var method = with.IsGenericMethodDefinition
                ? with.MakeGenericMethod(with.GetGenericArguments().Select(a => a.GetGenericParameterConstraints().Single()).ToArray())
                : with;
            var args = method.GetParameters().Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();

            var thrown = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, args));

            var argumentNull = Assert.IsType<ArgumentNullException>(thrown.InnerException);
            Assert.Equal(method.GetParameters()[0].Name, argumentNull.ParamName);
        }
    }

    private static bool Mirrors(MethodInfo with, MethodInfo add, bool allowDroppedOptionals)
    {
        if (with.Name != "With" + add.Name["Add".Length..] || !with.IsDefined(typeof(ExtensionAttribute)))
        {
            return false;
        }

        var withParameters = with.GetParameters();
        if (!IsReceiver(withParameters[0].ParameterType, add.DeclaringType!))
        {
            return false;
        }

        // The callback: present, last and optional exactly when the Add* hands back a child.
        var rest = withParameters[1..];
        if (add.ReturnType == typeof(void))
        {
            if (rest.Length > 0 && IsCallback(rest[^1].ParameterType))
            {
                return false;
            }
        }
        else
        {
            if (rest.Length == 0 || !IsCallback(rest[^1].ParameterType) || !rest[^1].IsOptional
                || !Corresponds(rest[^1].ParameterType.GetGenericArguments()[0], add.ReturnType))
            {
                return false;
            }

            rest = rest[..^1];
        }

        var addParameters = add.GetParameters();
        if (rest.Length > addParameters.Length
            || (rest.Length < addParameters.Length && !(allowDroppedOptionals && addParameters[rest.Length..].All(p => p.IsOptional))))
        {
            return false;
        }

        return rest.Zip(addParameters).All(pair => pair.First.Name == pair.Second.Name && Corresponds(pair.First.ParameterType, pair.Second.ParameterType));
    }

    /// <summary>The With's receiver is the Add's declaring type, or a type parameter standing for it.</summary>
    private static bool IsReceiver(Type receiver, Type declaring) =>
        receiver == declaring
        || (receiver.IsGenericParameter && receiver.GetGenericParameterConstraints().Contains(declaring))
        || (declaring.IsGenericTypeDefinition && receiver.IsGenericType && receiver.GetGenericTypeDefinition() == declaring);

    /// <summary>
    /// A With parameter corresponds to an Add parameter when it is the same type, a type parameter
    /// constrained to it (the callback then sees the caller's type), or — on a generic list — the
    /// list's own element type parameter.
    /// </summary>
    private static bool Corresponds(Type withType, Type addType) =>
        withType == addType
        || (withType.IsGenericParameter && addType.IsGenericParameter)
        || (withType.IsGenericParameter && withType.GetGenericParameterConstraints().Contains(addType));

    private static bool IsCallback(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Action<>);

    private static string Describe(MethodInfo method) =>
        $"{method.DeclaringType!.Name}.{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})";
}
