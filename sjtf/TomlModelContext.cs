using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Sjtf;

/// <summary>
/// Tomlyn 序列化上下文，用于 TOML 反序列化。
/// Tomlyn serialization context used for TOML deserialization.
/// </summary>
[TomlSerializable(typeof(TomlTable))]
[TomlSerializable(typeof(TomlArray))]
internal partial class TomlModelContext : TomlSerializerContext
{
}
