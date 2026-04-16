// Polyfill required for C# records targeting netstandard2.0.
// The compiler emits init-only setters using this type, which does not exist in netstandard2.0.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
