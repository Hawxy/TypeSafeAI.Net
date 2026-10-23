using System.Net;
using System.Text.Json.Nodes;
using TypeSafeAI.Tests.Fakes;

namespace TypeSafeAI.Tests.Images;

public class ImageTests
{
    private const string PngBase64 = "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAAHElEQVR4nGP4TyJgoKcGBgbsaFQDfTUQB4aDBgDAen2fpitsVAAAAABJRU5ErkJggg==";
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    private const string BoxResponse = """
        {"model":"jev-1.14.0","answers":{"mug":{"type":"box","box":{"top":0.25,"bottom":0.82,"left":0.44,"right":0.84},"confidence":0.92},
        "edge":{"type":"box","box":{"top":0.2,"bottom":0.66,"left":0.19,"right":0.68},"confidence":null}},"usage":{"input_tokens":290,"output_tokens":21}}
        """;

    [Test]
    public async Task Images_in_state_instructions_and_criteria_are_sent_as_multipart_parts()
    {
        var door = TypeSafeImage.FromBase64(PngBase64, reference: "ref-door");
        var badge = TypeSafeImage.FromBytes(JpegBytes, reference: "ref-badge");
        var thumb = TypeSafeImage.FromBytes(JpegBytes, reference: "ref-thumb");
        var handler = new FakeHttpMessageHandler().Ok(TestClient.NoulResponse);
        using var client = TestClient.Create(handler);

        await client.SystemOneAsync(new SystemOneRequest
        {
            State = TypeSafeContent.FromObject(new { door }),
            Questions = new Dictionary<string, Question>
            {
                ["badge"] = Question.Noul(TypeSafeContent.FromText($"Is the person in `door` the person in {badge}?")),
                ["gesture"] = Question.Choice("Which gesture?", new Dictionary<string, TypeSafeContent?> { ["thumbs_up"] = thumb, ["none"] = "No hand." }),
                ["door_again"] = Question.Noul(door),
            },
        });

        var request = handler.Requests[0];
        var parts = request.Parts;
        await Assert.That(request.Headers["TypeSafe-Danger-Zone-Images"]).IsEqualTo("true");
        await Assert.That(parts.Select(p => p.Name)).IsEquivalentTo(["request", "image-0", "image-1", "image-2"]);

        var json = JsonNode.Parse(request.Body!)!;
        await Assert.That(parts[0].ContentType).IsEqualTo("application/json; charset=utf-8");
        await Assert.That(parts[0].FileName).IsNull();
        await Assert.That(json["state"]!["door"]!.GetValue<string>()).IsEqualTo("ref-door");
        await Assert.That(json["questions"]!["badge"]!["instructions"]!.GetValue<string>()).IsEqualTo("Is the person in `door` the person in ref-badge?");
        await Assert.That(json["questions"]!["gesture"]!["criteria"]!["thumbs_up"]!.GetValue<string>()).IsEqualTo("ref-thumb");

        var expectedAssets = JsonNode.Parse("""
            {
              "ref-door": { "type": "image", "format": "png", "part": "image-0" },
              "ref-badge": { "type": "image", "format": "jpg", "part": "image-1" },
              "ref-thumb": { "type": "image", "format": "jpg", "part": "image-2" }
            }
            """);
        await Assert.That(JsonNode.DeepEquals(json["assets"], expectedAssets)).IsTrue();

        await Assert.That(parts[1].ContentType).IsEqualTo("image/png");
        await Assert.That(parts[1].FileName).IsEqualTo("image-0.png");
        await Assert.That(parts[1].Bytes).IsEquivalentTo(Convert.FromBase64String(PngBase64));
        await Assert.That(parts[2].ContentType).IsEqualTo("image/jpeg");
        await Assert.That(parts[2].Bytes).IsEquivalentTo(JpegBytes);
    }

    [Test]
    public async Task Retries_resend_the_image_parts()
    {
        var handler = new FakeHttpMessageHandler().Json((HttpStatusCode)529, "").Ok(TestClient.NoulResponse);
        using var client = TestClient.Create(handler);

        await client.SystemOneAsync(TypeSafeImage.FromBytes(JpegBytes), new Dictionary<string, Question> { ["q"] = Question.Noul("Is it a dog?") });

        await Assert.That(handler.Requests.Select(r => r.Parts.Count)).IsEquivalentTo([2, 2]);
    }

    [Test]
    public async Task Requests_without_images_or_boxes_stay_json_without_the_images_header()
    {
        var handler = new FakeHttpMessageHandler().Ok(TestClient.NoulResponse);
        using var client = TestClient.Create(handler);

        await client.SystemOneAsync(TestClient.UrgencyRequest());

        var request = handler.Requests[0];
        await Assert.That(request.Parts).IsEmpty();
        await Assert.That(request.Headers.ContainsKey("TypeSafe-Danger-Zone-Images")).IsFalse();
        await Assert.That(request.Headers.ContainsKey("TypeSafe-Danger-Zone-Long-Context")).IsFalse();
        await Assert.That(JsonNode.Parse(request.Body!)!["assets"]).IsNull();
    }

    [Test]
    public async Task The_same_image_used_twice_is_sent_once()
    {
        var image = TypeSafeImage.FromBytes(JpegBytes, reference: "ref-1");
        var handler = new FakeHttpMessageHandler().Ok(TestClient.NoulResponse);
        using var client = TestClient.Create(handler);

        await client.SystemOneAsync(image, new Dictionary<string, Question> { ["q"] = Question.Noul(TypeSafeContent.FromText($"Is {image} a dog?")) });

        await Assert.That(handler.Requests[0].Parts.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Different_images_with_one_reference_are_rejected()
    {
        var handler = new FakeHttpMessageHandler();
        using var client = TestClient.Create(handler);
        var first = TypeSafeImage.FromBytes(JpegBytes, reference: "same");
        var second = TypeSafeImage.FromBase64(PngBase64, reference: "same");

        await Assert.ThrowsAsync<ArgumentException>(() => client.SystemOneAsync(first, new Dictionary<string, Question> { ["q"] = Question.Noul(second) }));
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task Hand_written_references_are_sent_with_WithAssets()
    {
        var photo = TypeSafeImage.FromBytes(JpegBytes, reference: "ref-photo");
        var poster = TypeSafeImage.FromBytes(JpegBytes, reference: "ref-poster");
        var handler = new FakeHttpMessageHandler().Ok(TestClient.NoulResponse);
        using var client = TestClient.Create(handler);

        await client.SystemOneAsync(
            TypeSafeContent.FromJson("""{"photo":"ref-photo"}""").WithAssets(photo),
            new Dictionary<string, Question> { ["q"] = Question.Noul(TypeSafeContent.FromText("Same dog as ref-poster?").WithAssets(poster)) });

        var assets = JsonNode.Parse(handler.Requests[0].Body!)!["assets"]!.AsObject();
        await Assert.That(assets.Select(a => a.Key)).IsEquivalentTo(["ref-photo", "ref-poster"]);
    }

    [Test]
    public async Task Content_from_objects_and_interpolation_carries_its_images()
    {
        var image = TypeSafeImage.FromBytes(JpegBytes);

        var fromObject = TypeSafeContent.FromObject(new { frames = new[] { image, image } });
        var fromText = TypeSafeContent.FromText($"Compare {image} with {42}.");
        var fromImage = (TypeSafeContent)image;

        await Assert.That(fromObject.Node!["frames"]![0]!.GetValue<string>()).IsEqualTo(image.Reference);
        await Assert.That(fromObject.Assets.Count).IsEqualTo(2);
        await Assert.That(fromText.Text).IsEqualTo($"Compare {image.Reference} with 42.");
        await Assert.That(fromText.Assets).IsEquivalentTo([image]);
        await Assert.That(fromImage.Text).IsEqualTo(image.Reference);
        await Assert.That(fromImage.Assets).IsEquivalentTo([image]);
        await Assert.That(TypeSafeContent.FromText("plain").Assets).IsEmpty();
        await Assert.That(TypeSafeContent.FromObject(new { plain = 1 }).Assets).IsEmpty();
    }

    [Test]
    public async Task Images_detect_their_format_and_default_to_a_uuid_reference()
    {
        var png = TypeSafeImage.FromBase64(PngBase64);
        var jpeg = TypeSafeImage.FromBytes(JpegBytes);
        var unknown = TypeSafeImage.FromBytes(new byte[] { 1, 2, 3 });

        await Assert.That(png.Format).IsEqualTo(TypeSafeImageFormat.Png);
        await Assert.That(jpeg.Format).IsEqualTo(TypeSafeImageFormat.Jpeg);
        await Assert.That(unknown.Format).IsNull();
        await Assert.That(Guid.TryParse(png.Reference, out _)).IsTrue();
        await Assert.That(png.Reference).IsNotEqualTo(jpeg.Reference);
        await Assert.That(png.ToString()).IsEqualTo(png.Reference);
        await Assert.That(() => TypeSafeImage.FromBytes(ReadOnlyMemory<byte>.Empty)).Throws<ArgumentException>();
        await Assert.That(() => TypeSafeImage.FromBytes(JpegBytes, reference: " ")).Throws<ArgumentException>();
        await Assert.That(() => TypeSafeImage.FromBytes(JpegBytes, reference: new string('x', 1025))).Throws<ArgumentException>();
    }

    [Test]
    public async Task Streams_are_read_from_their_current_position()
    {
        using var seekable = new MemoryStream([0x00, .. JpegBytes]) { Position = 1 };

        var image = await TypeSafeImage.FromStreamAsync(seekable);

        await Assert.That(image.Data.ToArray()).IsEquivalentTo(JpegBytes);
        await Assert.That(image.Format).IsEqualTo(TypeSafeImageFormat.Jpeg);
    }

    [Test]
    public async Task Box_questions_serialise_and_send_the_images_header()
    {
        var handler = new FakeHttpMessageHandler().Ok(BoxResponse);
        using var client = TestClient.Create(handler);

        await client.SystemOneAsync("ref-photo", new Dictionary<string, Question> { ["mug"] = Question.Box("the coffee mug in `photo`") });

        var request = handler.Requests[0];
        await Assert.That(request.Headers["TypeSafe-Danger-Zone-Images"]).IsEqualTo("true");
        var expected = JsonNode.Parse("""{ "mug": { "type": "box", "instructions": "the coffee mug in `photo`" } }""");
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(request.Body!)!["questions"], expected)).IsTrue();
    }

    [Test]
    public async Task Box_answers_bind_with_coordinates_and_optional_confidence()
    {
        var handler = new FakeHttpMessageHandler().Ok(BoxResponse);
        using var client = TestClient.Create(handler);
        var questions = new QuestionSet();
        var mug = questions.Box("the coffee mug in `photo`", id: "mug");
        questions.Add("edge", Question.Box("the red rectangle"));

        var result = await client.SystemOneAsync(TypeSafeImage.FromBytes(JpegBytes), questions);

        var box = result.Get(mug);
        await Assert.That(box.Box.Top).IsEqualTo(0.25);
        await Assert.That(box.Box.Bottom).IsEqualTo(0.82);
        await Assert.That(box.Box.Left).IsEqualTo(0.44);
        await Assert.That(box.Box.Right).IsEqualTo(0.84);
        await Assert.That(box.Confidence).IsEqualTo(0.92);
        await Assert.That(box.Box.ToPixels(1000, 500)).IsEqualTo((440, 125, 840, 410));
        await Assert.That(result.Response.Boxes.Count).IsEqualTo(2);
        await Assert.That(result["edge"].AsBox().Confidence).IsNull();
        await Assert.That(() => result["edge"].AsNoul()).Throws<TypeSafeResponseValidationException>();
    }

    [Test]
    [Arguments(true, null, true)]
    [Arguments(false, true, true)]
    [Arguments(true, false, false)]
    public async Task Long_context_header_follows_client_and_request_options(bool clientSetting, bool? requestSetting, bool expected)
    {
        var handler = new FakeHttpMessageHandler().Ok(TestClient.NoulResponse);
        using var client = TestClient.Create(handler, o => o.LongContext = clientSetting);

        await client.SystemOneAsync(TestClient.UrgencyRequest(), new RequestOptions { LongContext = requestSetting });

        var headers = handler.Requests[0].Headers;
        await Assert.That(headers.ContainsKey("TypeSafe-Danger-Zone-Long-Context")).IsEqualTo(expected);
        if (expected)
        {
            await Assert.That(headers["TypeSafe-Danger-Zone-Long-Context"]).IsEqualTo("true");
        }
    }
}
