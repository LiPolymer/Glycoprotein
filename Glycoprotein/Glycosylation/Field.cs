using System.Text.Json;
using System.Text.Json.Serialization;

namespace Glycoprotein.Glycosylation;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
[JsonDerivedType(typeof(Field.Method),"Method")]
[JsonDerivedType(typeof(Field.Event),"Event")]
public abstract record Field {
    public required string Id { get; init; }

    public string? FriendlyName { get; init; }

    public string? Description { get; init; }

    public record Method : Field {
        public JsonElement? QuerySchema { get; init; }

        public JsonElement? ReceiptSchema { get; init; }

        [JsonIgnore] public bool IsAction { get => QuerySchema == null && ReceiptSchema == null; }

        [JsonIgnore] public bool IsActable { get => QuerySchema == null; }
    }

    public record Event : Field {
        public JsonElement? CallArgSchema { get; init; }
    }
}