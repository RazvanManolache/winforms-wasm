// Fork-owned C#13 spelling of the single C#14 extension-block file in the
// vendored sources (see runtimes/.../Ole/DataObjectExtensions.cs, excluded
// in the project). Identical behavior, classic extension-method syntax.

using System.Collections.Specialized;

namespace System.Private.Windows.Ole;

internal static class DataObjectExtensions
{
    internal static void SetFileDropList(this IComVisibleDataObject dataObject, StringCollection filePaths)
    {
        if (filePaths.OrThrowIfNull().Count == 0)
        {
            throw new ArgumentException(SR.CollectionEmptyException);
        }

        // Validate the paths to make sure they don't contain invalid characters
        string[] filePathsArray = new string[filePaths.Count];
        filePaths.CopyTo(filePathsArray, 0);

        foreach (string path in filePathsArray)
        {
            // These are the only error states for Path.GetFullPath
            if (string.IsNullOrEmpty(path) || path.Contains('\0'))
            {
                throw new ArgumentException(string.Format(SR.Clipboard_InvalidPath, path ?? "<null>", nameof(filePaths)));
            }
        }

        dataObject.SetData(DataFormatNames.FileDrop, autoConvert: true, filePathsArray);
    }
}
