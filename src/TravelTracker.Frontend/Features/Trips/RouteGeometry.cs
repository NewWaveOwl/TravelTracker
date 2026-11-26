namespace TravelTracker.Frontend.Features.Trips;

/** Decodes bounded Google-format provider polylines into validated map coordinates. */
public static class RouteGeometry
{
    /** Combines provider sections into a single bounded polyline5 for a saved connection. */
    public static string? Combine(IEnumerable<string> sections)
    {
        var path = new System.Text.StringBuilder();
        int latitude = 0, longitude = 0;
        foreach (var section in sections)
        {
            var points = Decode(section);
            if (points.Count < 2) return null;
            foreach (var point in points)
            {
                var lat = (int)(point.Lat * 100000);
                var lon = (int)(point.Lon * 100000);
                AppendDelta(lat - latitude);
                AppendDelta(lon - longitude);
                (latitude, longitude) = (lat, lon);
                if (path.Length > 200_000) return null;
            }
        }
        return path.Length == 0 ? null : path.ToString();

        void AppendDelta(int delta)
        {
            var value = (delta << 1) ^ (delta >> 31);
            while (value >= 32)
            {
                path.Append((char)(((value & 31) | 32) + 63));
                value >>= 5;
            }
            path.Append((char)(value + 63));
        }
    }

    /**
     * Decodes provider geometry without accepting incomplete or out-of-range coordinates.
     * @param encoded An encoded polyline with five decimal places.
     * @return The complete path, or an empty path when the data is malformed.
     */
    public static IReadOnlyList<(decimal Lat, decimal Lon)> Decode(string encoded)
    {
        if (encoded.Length > 200_000) return [];
        var points = new List<(decimal, decimal)>();
        var index = 0;
        long latitude = 0, longitude = 0;
        while (index < encoded.Length)
        {
            if (!Read(out var lat) || !Read(out var lon)) return [];
            latitude += lat;
            longitude += lon;
            if (latitude is < -9_000_000 or > 9_000_000 || longitude is < -18_000_000 or > 18_000_000) return [];
            points.Add((latitude / 100000m, longitude / 100000m));
        }
        return points;

        bool Read(out long delta)
        {
            long value = 0;
            var shift = 0;
            delta = 0;
            while (index < encoded.Length && shift <= 30)
            {
                var part = encoded[index++] - 63;
                if (part is < 0 or > 63) return false;
                value |= (long)(part & 31) << shift;
                if ((part & 32) == 0)
                {
                    delta = (value & 1) != 0 ? ~(value >> 1) : value >> 1;
                    return true;
                }
                shift += 5;
            }
            return false;
        }
    }
}
