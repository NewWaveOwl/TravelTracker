using System.Globalization;

namespace TravelTracker.Frontend.Features;

/** shared numeric range check for the form drafts, so every draft reads numbers the same way. */
public static class RangeRules
{
    /**
     * checks that text is an invariant-culture decimal inside an inclusive range.
     * @param value The typed text.
     * @param minimum The smallest allowed value.
     * @param maximum The largest allowed value.
     * @return True when the text parses and falls inside the range.
     */
    public static bool IsDecimalInRange(string value, decimal minimum, decimal maximum) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number >= minimum && number <= maximum;
}
