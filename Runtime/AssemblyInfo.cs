// Grant the test project access to `internal` types like
// `DataEnvelope<T>` so tests can drive the SDK against representative
// wire shapes without us having to widen the public API.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Kraty.SDK.Tests")]
