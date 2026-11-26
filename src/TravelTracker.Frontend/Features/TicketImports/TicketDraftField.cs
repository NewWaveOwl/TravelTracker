namespace TravelTracker.Frontend.Features.TicketImports;

/**
 * An editable supported ticket field that keeps its extraction uncertainty visible.
 * @param name Stable HTTP field name.
 * @param label Accessible display label.
 * @param required Whether a reviewed draft needs a value.
 * @param inputType Native input type.
 */
public sealed class TicketDraftField(string name, string label, bool required, string inputType = "text")
{
    public string Name { get; } = name;
    public string Label { get; } = label;
    public bool Required { get; } = required;
    public string InputType { get; } = inputType;
    public string Value { get; set; } = "";
    public string Status { get; set; } = "missing";
    public string? Error { get; set; }
    public string Hint => Error ?? Status switch
    {
        "missing" => "Not found in this document. Enter it manually if needed.",
        "uncertain" => "Check against your ticket before finishing your review.",
        _ => "Compare with the original ticket."
    };
}
