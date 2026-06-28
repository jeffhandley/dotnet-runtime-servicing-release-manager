// (mock servicing fix excerpt) ValueType::GetHashCode generic-context fix
// Ensures the runtime uses the exact instantiation (not __Canon) when computing
// the default hash for value types that carry a generic field.
// Real change: dotnet/runtime#129728 / backport #129744.
