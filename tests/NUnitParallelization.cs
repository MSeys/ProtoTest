using NUnit.Framework;

// Linked into every NUnit test project that can run in parallel, so the policy is stated once. A
// project that stays single-threaded states why in its commit instead of linking this file.
[assembly: LevelOfParallelism(8)]
[assembly: Parallelizable(ParallelScope.All)]
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
