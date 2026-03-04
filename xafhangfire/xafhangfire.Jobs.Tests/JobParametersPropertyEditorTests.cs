using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using xafhangfire.Blazor.Server.Editors;

namespace xafhangfire.Jobs.Tests;

public class JobParametersPropertyEditorTests
{
    [Fact]
    public void SerializeFieldsToJson_JsonField_PreservesStructuredJson()
    {
        var fields = new List<ParameterFieldModel>
        {
            new()
            {
                Name = "AttachmentPaths",
                FieldType = "json",
                StringValue = """["first.pdf","second.pdf"]"""
            }
        };

        var json = InvokeSerializeFieldsToJson(fields);
        using var document = JsonDocument.Parse(json);

        var property = document.RootElement.GetProperty("AttachmentPaths");
        property.ValueKind.Should().Be(JsonValueKind.Array);
        property.EnumerateArray().Select(x => x.GetString()).Should().Equal("first.pdf", "second.pdf");
    }

    private static string InvokeSerializeFieldsToJson(List<ParameterFieldModel> fields)
    {
        var method = typeof(JobParametersPropertyEditor).GetMethod(
            "SerializeFieldsToJson",
            BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object[] { fields });
        result.Should().BeOfType<string>();
        return (string)result!;
    }
}
