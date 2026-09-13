using Xunit.Sdk;
using Xunit.v3;

// These tests observe and mutate process-wide state: which assemblies the CLI path has loaded, and the
// Console.Out/Console.Error writers the modes write through. Running them concurrently would let one test
// swap the console out from under another, or load an assembly the lightweight-path test is checking for.
[assembly: Parallelization(Mode = ParallelMode.None)]
