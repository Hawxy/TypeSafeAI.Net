using System.Text.Json;
using System.Text.Json.Nodes;

namespace TypeSafeAI.Tests.Json;

public class ContentTests
{
    [Test]
    public async Task Text_and_json_content_are_distinguished()
    {
        TypeSafeContent text = "hello";
        var json = TypeSafeContent.FromNode(new JsonObject { ["a"] = 1 });

        await Assert.That(text.IsText).IsTrue();
        await Assert.That(text.Text).IsEqualTo("hello");
        await Assert.That(json.IsJson).IsTrue();
        await Assert.That(json.ToString()).IsEqualTo("""{"a":1}""");
        await Assert.That(default(TypeSafeContent).IsEmpty).IsTrue();
    }

    [Test]
    public async Task From_json_keeps_strings_as_text()
    {
        var content = TypeSafeContent.FromJson("\"just text\"");
        await Assert.That(content.IsText).IsTrue();
        await Assert.That(content.Text).IsEqualTo("just text");
    }

    [Test]
    public async Task From_element_clones_the_value()
    {
        using var document = JsonDocument.Parse("""{"k":[1,2]}""");
        var content = TypeSafeContent.FromElement(document.RootElement);
        await Assert.That(content.Node!["k"]!.AsArray().Count).IsEqualTo(2);
    }

    [Test]
    public async Task Equality_compares_text_or_deep_json()
    {
        await Assert.That((TypeSafeContent)"a" == "a").IsTrue();
        await Assert.That((TypeSafeContent)"a" == "b").IsFalse();
        await Assert.That(TypeSafeContent.FromJson("""{"a":1}""") == TypeSafeContent.FromJson("""{"a":1}""")).IsTrue();
        await Assert.That(TypeSafeContent.FromJson("""{"a":1}""") == "a").IsFalse();
    }

    [Test]
    public async Task Reflection_from_object_uses_camel_case()
    {
#pragma warning disable IL2026, IL3050 // test code opts into reflection serialization
        var content = TypeSafeContent.FromObject(new { OrderId = "A1", Total = 12.5 });
#pragma warning restore IL2026, IL3050
        await Assert.That(content.Node!["orderId"]!.GetValue<string>()).IsEqualTo("A1");
    }
}
