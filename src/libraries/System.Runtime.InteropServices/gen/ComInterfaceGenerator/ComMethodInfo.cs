// (mock servicing fix excerpt) De-duplicate inherited COM methods by full signature
// instead of method name, so two disjoint base interfaces that each declare a method
// of the same name no longer collide. Real change: dotnet/runtime#129473.
