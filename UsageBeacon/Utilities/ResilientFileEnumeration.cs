using System.IO;

namespace UsageBeacon.Utilities;

/// <summary>
/// Guards lazy filesystem enumerations. A directory that becomes unreadable is
/// reported from <see cref="IEnumerator.MoveNext"/>, not from the call that
/// creates the sequence, so a plain try/catch around the creating call leaves
/// the iteration unguarded and one bad directory aborts the whole traversal.
/// </summary>
internal static class ResilientFileEnumeration
{
    /// <summary>
    /// Enumerates a filesystem sequence, ending it quietly at the first I/O or
    /// access failure instead of letting that failure reach the caller. Items
    /// produced before the failure are still returned. Any other exception
    /// propagates, because it signals a defect rather than a hostile filesystem.
    /// </summary>
    public static IEnumerable<T> IgnoringFileSystemErrors<T>(Func<IEnumerable<T>> source)
    {
        IEnumerator<T> enumerator;
        try
        {
            enumerator = source().GetEnumerator();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        try
        {
            while (true)
            {
                T current;
                try
                {
                    if (!enumerator.MoveNext()) yield break;
                    current = enumerator.Current;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    yield break;
                }

                // Outside the try block: C# forbids yielding from a try that has a catch.
                yield return current;
            }
        }
        finally
        {
            enumerator.Dispose();
        }
    }
}
