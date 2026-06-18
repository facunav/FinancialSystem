// Compatibility stubs to satisfy Microsoft.AspNetCore.OpenApi source generator
// The real types are provided by the framework in some SDK versions; in this
// project the generator emits references to these symbols but they are not
// resolved. Minimal stubs here allow the project to compile.

namespace Microsoft.AspNetCore.OpenApi
{
    // Minimal marker interfaces expected by the source generator
    public interface IOpenApiOperationTransformer { }
    public interface IOpenApiSchemaTransformer { }

    // Minimal context types referenced by the generator
    public sealed class OpenApiOperationTransformerContext
    {
        // Intentionally empty stub
    }

    public sealed class OpenApiSchemaTransformerContext
    {
        // Intentionally empty stub
    }
}
