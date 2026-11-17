using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Oficina.Billing;

internal static class SqsTraceContext
{
    public const string SourceName = "Oficina.Billing.Sqs";
    private static readonly ActivitySource Source = new(SourceName);

    public static Activity Start(string operation, ActivityKind kind, string? parent)
    {
        var validParent = ActivityContext.TryParse(parent, null, out var context);
        var activity = validParent
            ? Source.StartActivity(operation, kind, context)
            : Source.StartActivity(operation, kind);
        if (activity is not null) return activity;
        activity = new Activity(operation).SetIdFormat(ActivityIdFormat.W3C);
        if (validParent) activity.SetParentId(parent!);
        return activity.Start();
    }

    public static string Stamp(string envelopeJson)
    {
        var parent = Activity.Current?.Id;
        if (!ActivityContext.TryParse(parent, null, out _)) return envelopeJson;
        var envelope = JsonNode.Parse(envelopeJson)?.AsObject()
            ?? throw new JsonException("Envelope de integração inválido.");
        envelope["traceparent"] = parent;
        return envelope.ToJsonString();
    }

    public static string? Read(string envelopeJson)
    {
        using var document = JsonDocument.Parse(envelopeJson);
        if (!document.RootElement.TryGetProperty("traceparent", out var value) ||
            value.ValueKind != JsonValueKind.String)
            return null;
        var parent = value.GetString();
        return ActivityContext.TryParse(parent, null, out _) ? parent : null;
    }
}
