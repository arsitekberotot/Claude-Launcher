using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

class UsageWindow
{
    public double? Utilization; // 0..1
    public DateTime? ResetsAt;  // UTC

    public bool HasReset { get { return ResetsAt.HasValue && ResetsAt.Value <= DateTime.UtcNow; } }
}

class UsageSnapshot
{
    public UsageWindow FiveHour, Weekly;
    public DateTime Taken; // UTC
}

// Last rate-limit snapshot Claude cached with a conversation. It is NOT live: Claude fetches
// current usage from the server and does not store it, so this can be days old.
static class UsageInfo
{
    static readonly Dictionary<string, KeyValuePair<DateTime, UsageSnapshot>> cache =
        new Dictionary<string, KeyValuePair<DateTime, UsageSnapshot>>(StringComparer.OrdinalIgnoreCase);
    static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
    static readonly Regex IsoTime = new Regex(@"20\d\d-\d\d-\d\dT\d\d:\d\d:\d\d(\.\d+)?Z");

    public static UsageSnapshot Latest(string dataDir)
    {
        try
        {
            var blobDir = Path.Combine(dataDir, "IndexedDB", "https_claude.ai_0.indexeddb.blob");
            if (!Directory.Exists(blobDir)) return null;
            var files = new DirectoryInfo(blobDir).GetFiles("*", SearchOption.AllDirectories)
                .Where(f => f.Length < 50 * 1024 * 1024).ToList();
            var stamp = files.Count == 0 ? DateTime.MinValue : files.Max(f => f.LastWriteTimeUtc);
            KeyValuePair<DateTime, UsageSnapshot> hit;
            if (cache.TryGetValue(dataDir, out hit) && hit.Key == stamp) return hit.Value;

            UsageSnapshot best = null;
            foreach (var f in files)
            {
                var raw = Unwrap(ReadShared(f.FullName));
                if (raw == null) continue;
                var found = Parse(raw, Latin1.GetString(raw));
                if (found != null && (best == null || found.Taken > best.Taken)) best = found;
            }
            cache[dataDir] = new KeyValuePair<DateTime, UsageSnapshot>(stamp, best);
            return best;
        }
        catch { return null; }
    }

    static byte[] ReadShared(string path)
    {
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var ms = new MemoryStream())
        {
            fs.CopyTo(ms);
            return ms.ToArray();
        }
    }

    // Chromium IndexedDB values: 0xFF <version> then 0x02 = Snappy-compressed V8 data.
    static byte[] Unwrap(byte[] data)
    {
        if (data.Length < 4 || data[0] != 0xFF) return null;
        if (data[2] == 0x02)
        {
            try { return Snappy(data, 3); } catch { return null; }
        }
        return data;
    }

    // Raw (unframed) Snappy block decoder.
    static byte[] Snappy(byte[] src, int pos)
    {
        int length = 0, shift = 0;
        while (true)
        {
            int b = src[pos++];
            length |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        var dst = new byte[length];
        int d = 0;
        while (pos < src.Length && d < length)
        {
            int tag = src[pos++];
            int type = tag & 3;
            if (type == 0)
            {
                int n = tag >> 2;
                if (n >= 60)
                {
                    int bytes = n - 59;
                    n = 0;
                    for (int i = 0; i < bytes; i++) n |= src[pos++] << (8 * i);
                }
                n += 1;
                Buffer.BlockCopy(src, pos, dst, d, n);
                pos += n;
                d += n;
            }
            else
            {
                int n, offset;
                if (type == 1) { n = ((tag >> 2) & 7) + 4; offset = ((tag >> 5) << 8) | src[pos++]; }
                else if (type == 2) { n = (tag >> 2) + 1; offset = src[pos] | (src[pos + 1] << 8); pos += 2; }
                else { n = (tag >> 2) + 1; offset = BitConverter.ToInt32(src, pos); pos += 4; }
                if (offset <= 0 || offset > d) throw new InvalidDataException();
                for (int i = 0; i < n; i++, d++) dst[d] = dst[d - offset];
            }
        }
        return dst;
    }

    // rate_limit_info.unifiedWindows = { five_hour: { resetsAt, status, utilization }, seven_day: { ... } }
    static UsageSnapshot Parse(byte[] raw, string s)
    {
        UsageSnapshot best = null;
        const string marker = "unifiedWindows";
        for (int i = s.IndexOf(marker, StringComparison.Ordinal); i >= 0; i = s.IndexOf(marker, i + 1, StringComparison.Ordinal))
        {
            int end = Math.Min(s.Length, i + 600);
            var five = Window(raw, s, i, end, "five_hour");
            var week = Window(raw, s, i, end, "seven_day");
            if (five == null && week == null) continue;
            var taken = Timestamp(s, i);
            if (!taken.HasValue) continue;
            if (best == null || taken.Value > best.Taken)
                best = new UsageSnapshot { FiveHour = five, Weekly = week, Taken = taken.Value };
        }
        return best;
    }

    // V8 object: key string, then the value ('o' starts an object, '{' ends it).
    static UsageWindow Window(byte[] raw, string s, int from, int end, string name)
    {
        int k = s.IndexOf(name + "o", from, end - from, StringComparison.Ordinal);
        if (k < 0) return null;
        int objEnd = s.IndexOf('{', k);
        if (objEnd < 0 || objEnd > end) objEnd = end;
        var w = new UsageWindow { Utilization = Number(raw, s, k, objEnd, "utilization") };
        var reset = Number(raw, s, k, objEnd, "resetsAt");
        if (reset.HasValue)
        {
            var seconds = reset.Value > 1e12 ? reset.Value / 1000 : reset.Value;
            w.ResetsAt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);
        }
        if (w.Utilization.HasValue && w.Utilization.Value > 1.0) w.Utilization = w.Utilization.Value / 100;
        return w.Utilization.HasValue || w.ResetsAt.HasValue ? w : null;
    }

    // 'N' = double (8 bytes LE), 'I' = zigzag varint int32.
    static double? Number(byte[] raw, string s, int from, int end, string key)
    {
        int k = s.IndexOf(key, from, Math.Max(0, end - from), StringComparison.Ordinal);
        if (k < 0) return null;
        int p = k + key.Length;
        if (p >= raw.Length) return null;
        if (raw[p] == (byte)'N' && p + 9 <= raw.Length) return BitConverter.ToDouble(raw, p + 1);
        if (raw[p] == (byte)'I')
        {
            int v = 0, shift = 0;
            for (p++; p < raw.Length && shift < 35; p++, shift += 7)
            {
                v |= (raw[p] & 0x7F) << shift;
                if ((raw[p] & 0x80) == 0) break;
            }
            return (v >> 1) ^ -(v & 1);
        }
        return null;
    }

    static DateTime? Timestamp(string s, int at)
    {
        var after = IsoTime.Match(s, at, Math.Min(900, s.Length - at));
        if (after.Success) return Parse(after.Value);
        int start = Math.Max(0, at - 600);
        var before = IsoTime.Matches(s.Substring(start, at - start)).Cast<Match>().LastOrDefault();
        return before == null ? (DateTime?)null : Parse(before.Value);
    }

    static DateTime? Parse(string iso)
    {
        DateTime t;
        return DateTime.TryParse(iso, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t) ? t : (DateTime?)null;
    }
}
