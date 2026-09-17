using TypeSafeAI;

// Ticket triage: three typed judgments about one support ticket, then confidence-gated routing.
// Set TYPESAFE_API_KEY before running. Publishes with PublishAot=true.

var ticket = args.Length > 0
    ? string.Join(' ', args)
    : "Help! My payouts have been failing for 3 days and nobody answers my emails.";

var q = new QuestionSet();
var category = q.Choice<TicketCategory>("What is this support ticket about?");
var urgent = q.Noul("Does the customer convey urgency?", yes: "Explicitly time-sensitive or blocking", no: "No time pressure expressed");
var anger = q.Score<Anger>("How angry is the customer?");

if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(TypeSafeClientOptions.ApiKeyEnvironmentVariable)))
{
    Console.WriteLine($"Built {q.Count} questions ({string.Join(", ", q.Keys)}).");
    Console.WriteLine($"Set {TypeSafeClientOptions.ApiKeyEnvironmentVariable} to send them to the API.");
    return 0;
}

try
{
    using var client = new TypeSafeClient();
    var result = await client.SystemOneAsync(ticket, q);

    var c = result.Get(category);
    var u = result.Get(urgent);
    var a = result.Get(anger);

    Console.WriteLine($"Ticket:    {ticket}");
    Console.WriteLine($"Category:  {c.Choice} (confidence {c.Confidence:P0})");
    foreach (var (label, p) in c.Ranked())
    {
        Console.WriteLine($"           {label,-10} {p:P0}");
    }

    Console.WriteLine($"Urgent:    {u.Probability:P0}");
    Console.WriteLine($"Anger:     {a.Score:F2} ~ {a.Nearest} (confidence {a.Confidence:P0})");
    Console.WriteLine($"Model:     {result.Model}, request {result.RequestId}, {result.Usage?.InputTokens} input tokens");
    Console.WriteLine();

    // Confidence-gated routing, as in the TypeSafe docs: act only when confident, escalate otherwise.
    var route = c.Confidence switch
    {
        < 0.6 => "Route to a human: the category is uncertain.",
        _ when c.Choice == TicketCategory.Billing && u.IsYes(0.7) => "Open a priority billing case.",
        _ when c.Choice == TicketCategory.Billing => "Open a billing case.",
        _ when c.Choice == TicketCategory.Technical => "Send to the on-call engineer.",
        _ => "Reply with the general help article.",
    };
    if (a.IsAtLeast(Anger.Hostile))
    {
        route += " Flag for a senior agent because the customer is hostile.";
    }

    Console.WriteLine(route);
    return 0;
}
catch (TypeSafeException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

enum TicketCategory
{
    [Label("billing", Description = "Charges, refunds, invoices, payouts")]
    Billing,

    [Label("technical", Description = "Errors, outages, integration problems")]
    Technical,

    [Label("other", Description = "Anything else")]
    Other,
}

enum Anger
{
    [Label("calm", Description = "Calm and polite")]
    Calm,

    [Label("frustrated", Description = "Frustrated but civil")]
    Frustrated,

    [Label("hostile", Description = "Hostile or abusive")]
    Hostile,
}
