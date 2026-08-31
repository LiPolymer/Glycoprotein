using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Glycoprotein.Connexon;
using Glycoprotein.Conductors;
using Glycoprotein.Glycosylation;
using Xunit;

namespace Glycoprotein.Tests;

public class SchemaAnnotationTests {
    sealed record AnnotatedParams(
        [property: Display(Name = "用户名", Description = "登录用户名")]
        string UserName,

        [property: Description("重试次数")]
        int RetryCount,

        string PlainValue
    );

    [Fact]
    public void QuerySchema_InjectsStandardAnnotations() {
        ResponseConductor conductor = new(new UnixDomainMeshConnexon("schema-annotation-test"),"schema-annotation-test");
        try {
            conductor.AddAction<AnnotatedParams>(new Field.Method { Id = "echo" },_ => { });

            Field.Method field = Assert.IsType<Field.Method>(conductor.Fields.Single());
            JsonObject schema = JsonNode.Parse(field.QuerySchema!.Value.GetRawText())!.AsObject();
            JsonObject props = schema["properties"]!.AsObject();

            JsonObject userName = props["UserName"]!.AsObject();
            Assert.Equal("用户名",(string?)userName["title"]);
            Assert.Equal("登录用户名",(string?)userName["description"]);

            JsonObject retryCount = props["RetryCount"]!.AsObject();
            Assert.Equal("重试次数",(string?)retryCount["title"]);
            Assert.Null((string?)retryCount["description"]);

            JsonObject plainValue = props["PlainValue"]!.AsObject();
            Assert.Null((string?)plainValue["title"]);
            Assert.Null((string?)plainValue["description"]);
        } finally {
            conductor.Dispose();
        }
    }
}
