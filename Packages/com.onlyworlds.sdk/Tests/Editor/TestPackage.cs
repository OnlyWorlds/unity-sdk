using System;
using System.IO;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>Where the tests find the package's own files.</summary>
    internal static class TestPackage
    {
        /// <summary>
        /// The package's own resolved path. Outside the Editor (a compile and test run with no engine, where the
        /// package manager cannot be asked) the working directory's <c>Packages/com.onlyworlds.sdk</c>, when it is
        /// there; null when neither is.
        /// </summary>
        public static string Root()
        {
            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TestPackage).Assembly);
                if (package != null) return package.resolvedPath;
            }
            catch (Exception)
            {
                // No Editor behind the call; try the working directory below.
            }

            var local = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "com.onlyworlds.sdk");
            return Directory.Exists(local) ? local : null;
        }
    }
}
