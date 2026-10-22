using System.Runtime.CompilerServices;

// The tests reach the zip writer behind OWWorldArchive.Pack, to write zip64 without 65,535 files on disk.
[assembly: InternalsVisibleTo("OnlyWorlds.Sdk.Tests.Editor")]
