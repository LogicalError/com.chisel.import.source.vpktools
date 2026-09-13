// The FBX writer's pieces are internal - they are an implementation detail of the importer, not an
// API - but they are also where the subtle mistakes live (coordinate handedness, Euler order), so
// the tests need to reach them directly rather than only through an exported file.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("com.chisel.import.source.vpktools.tests")]
