// Reflect only the reviewed test assembly; never call scenario bodies or Unity internals.
namespace WhimTex.Tests
{
    public static class EntryContract
    {
        // Primitive-only wire format avoids registering ephemeral DTO array types
        // with Unity's serializer: one fully qualified method|argument-count per line.
        public static string Verify(string declarations) => TestContext.Run("Reviewed test entry contracts (NOT domain execution)", context =>
        {
            context.True(!string.IsNullOrEmpty(declarations), "Explicit lifecycle phases supplied");
            foreach (var declaration in declarations.Split('\n'))
            {
                int separator = declaration.LastIndexOf('|');
                context.True(separator > 0, "Explicit argument count supplied");
                string entry = declaration.Substring(0, separator);
                context.True(int.TryParse(declaration.Substring(separator + 1), out int arguments) && arguments >= 0, "Nonnegative argument count");
                int split = entry.LastIndexOf('.');
                context.True(split > 0, "Fully qualified test method supplied");
                var type = typeof(EntryContract).Assembly.GetType(entry.Substring(0, split));
                context.True(type != null, entry + ": declaring test type exists");
                System.Reflection.MethodInfo found = null;
                int count = 0;
                foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                    if (method.Name == entry.Substring(split + 1)) { found = method; count++; }
                context.Equal(1, count, entry + ": Pipeline requires a unique static method, including private overloads");
                context.True(found.IsPublic && !found.ContainsGenericParameters, entry + ": public non-generic entry");
                var parameters = found.GetParameters();
                context.True(parameters.Length >= arguments, entry + ": argument count fits");
                for (int i = arguments; i < parameters.Length; i++)
                    context.True(parameters[i].IsOptional, entry + ": omitted argument is optional");
            }
        });
    }
}
