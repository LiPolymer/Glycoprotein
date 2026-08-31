using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Json.Schema;
using Json.Schema.Generation;

namespace Glycoprotein.Glycosylation;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
[JsonDerivedType(typeof(Glycosyl.Beacon),"Beacon")]
[JsonDerivedType(typeof(Glycosyl.Query),"Query")]
[JsonDerivedType(typeof(Glycosyl.Reply),"Reply")]
[JsonDerivedType(typeof(Glycosyl.Event),"Event")]
[JsonDerivedType(typeof(Glycosyl.Heartbeat),"Heartbeat")]
public abstract class Glycosyl {
    public const int ProtocolVersion = 1;

    public static readonly JsonSerializerOptions Jso = new JsonSerializerOptions {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public sealed class Beacon : Glycosyl {
        public required string Id { get; init; }
        
        public string? Vendor { get; init; }
        
        public int? ProtocolVersion { get; init; }

        public required List<Field> Fields { get; init; }

        public DateTime Timestamp { get; } = DateTime.UtcNow;
    }

    public sealed class Query : Glycosyl {
        public required string Gid { get; init; }

        public string? SourceGid { get; init; }

        public required string Fid { get; init; }

        public JsonElement? Payload { get; init; }

        public Guid Qid { get; init; } = Guid.NewGuid();
    }

    public sealed class Reply : Glycosyl {
        public JsonElement? Payload { get; init; }

        public required Guid Qid { get; init; }

        public string? TargetGid { get; init; }

        public string? Error { get; init; }
    }

    public sealed class Event : Glycosyl {
        public required string Gid { get; init; }

        public required string Fid { get; init; }

        public JsonElement? Arg { get; set; }
    }

    /// <summary>
    /// 生成参数类型 schema, 并注入标准特性注解: [Display(Name, Description)] -> title + description, [Description] -> title。
    /// </summary>
    public static JsonElement GenerateSchema<T>() {
        JsonSchema schema = new JsonSchemaBuilder().FromType<T>().Build();
        JsonObject root = JsonNode.Parse(JsonSerializer.Serialize(schema))!.AsObject();
        if (root["properties"] is JsonObject props) {
            foreach (PropertyInfo member in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                string? title = null;
                string? description = null;
                if (member.GetCustomAttribute<DisplayAttribute>() is { } display) {
                    title = display.Name;
                    description = display.Description;
                } else if (member.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>() is { } desc) {
                    title = desc.Description;
                }
                if (title == null && description == null) continue;
                if (props[member.Name] is not JsonObject propNode) continue;
                if (title != null) propNode["title"] = title;
                if (description != null) propNode["description"] = description;
            }
        }
        string json = root.ToJsonString();
        using JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public static JsonElement SerializeToJsonElement<T>(T? value) {
        string json = JsonSerializer.Serialize(value, Jso);
        using JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// beacon 内容未变时的心跳, 仅刷新存活, 不携带字段数据。
    /// </summary>
    public sealed class Heartbeat : Glycosyl {
        public required string Id { get; init; }
    }

    public byte[] ToBytes() {
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this));
    }

    public static Glycosyl FromBytes(byte[] bytes) {
        return JsonSerializer.Deserialize<Glycosyl>(Encoding.UTF8.GetString(bytes))!;
    }
}