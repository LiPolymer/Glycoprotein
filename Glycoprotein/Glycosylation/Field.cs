using System.Text.Json;
using System.Text.Json.Serialization;

namespace Glycoprotein.Glycosylation;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
[JsonDerivedType(typeof(Field.Action), "Action")]
[JsonDerivedType(typeof(Field.Function), "Function")]
[JsonDerivedType(typeof(Field.Event), "Event")]
public abstract class Field {
    public required string Id { get; init; }
    public string? FriendlyName { get; set; }
    public string? Description { get; set; }
    
    public class Action : Field { }

    public class Function : Field {
        public JsonElement? QuerySchema { get; set; }
        public JsonElement? ReceiptSchema { get; set; }
    }

    public class Event : Field {
        public JsonElement? CallArgSchema { get; set; }
    }
}