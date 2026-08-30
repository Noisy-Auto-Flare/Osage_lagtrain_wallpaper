using System.Runtime.InteropServices;

namespace OsageLagtrain.App.Cycles;

/// <summary>Cycle validation error with path:line semantics. Never silently ignored.</summary>
public class CycleValidationException : SchemaValidationException
{
    public CycleValidationException(string message, string jsonPath) : base(message, jsonPath) { }
    public CycleValidationException(string message, string jsonPath, Exception inner) : base(message, jsonPath, inner) { }
}

/// <summary>Alias required by spec: CycleValidationError</summary>
public class CycleValidationError : CycleValidationException
{
    public CycleValidationError(string message, string jsonPath) : base(message, jsonPath) { }
    public CycleValidationError(string message, string jsonPath, Exception inner) : base(message, jsonPath, inner) { }
}

/// <summary>Loaded cycle scene: Id, title, config, frames sorted natural, dir path + mtime.</summary>
public sealed record CycleInfo
{
    public required string Id { get; init; }
    public string? Title { get; init; }
    public required SceneConfig Config { get; init; }
    public required IReadOnlyList<string> Frames { get; init; }
    public required string DirPath { get; init; }
    public DateTime Mtime { get; init; }
}

/// <summary>Natural sort via StrCmpLogicalW (shlwapi). Fallback to managed natural comparer if unavailable.</summary>
public static class NaturalSort
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int StrCmpLogicalW(string s1, string s2);

    public sealed class ComparerImpl : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;
            try
            {
                return StrCmpLogicalW(x, y);
            }
            catch (DllNotFoundException)
            {
                return FallbackCompare(x, y);
            }
            catch (EntryPointNotFoundException)
            {
                return FallbackCompare(x, y);
            }
            catch
            {
                return FallbackCompare(x, y);
            }
        }

        internal static int FallbackCompare(string a, string b)
        {
            int ia = 0, ib = 0;
            while (ia < a.Length && ib < b.Length)
            {
                bool da = char.IsDigit(a[ia]);
                bool db = char.IsDigit(b[ib]);
                if (da && db)
                {
                    int sa = ia, sb = ib;
                    while (ia < a.Length && char.IsDigit(a[ia])) ia++;
                    while (ib < b.Length && char.IsDigit(b[ib])) ib++;
                    var numA = a.Substring(sa, ia - sa);
                    var numB = b.Substring(sb, ib - sb);
                    // strip leading zeros for numeric value comparison but keep length for stable
                    var trimA = numA.TrimStart('0');
                    var trimB = numB.TrimStart('0');
                    if (trimA.Length == 0) trimA = "0";
                    if (trimB.Length == 0) trimB = "0";
                    if (trimA.Length != trimB.Length)
                        return trimA.Length.CompareTo(trimB.Length);
                    int numCmp = string.Compare(trimA, trimB, StringComparison.Ordinal);
                    if (numCmp != 0) return numCmp;
                    // equal numeric value -> fewer leading zeros (shorter raw) first to mimic StrCmpLogicalW
                    if (numA.Length != numB.Length)
                        return numA.Length.CompareTo(numB.Length);
                }
                else
                {
                    int cmp = char.ToLowerInvariant(a[ia]).CompareTo(char.ToLowerInvariant(b[ib]));
                    if (cmp != 0)
                        return a[ia].CompareTo(b[ib]);
                    ia++; ib++;
                }
            }
            return a.Length.CompareTo(b.Length);
        }
    }

    public static IComparer<string> Comparer { get; } = new ComparerImpl();
}
