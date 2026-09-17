namespace ProtoTest.AspNetCore;

/// <summary>Controls how long an in-process ASP.NET Core application lives.</summary>
public enum AspNetCoreServerLifetime
{
    /// <summary>
    /// One application instance serves every test in the run. Tests share the application's state,
    /// so isolate data per test (for example with test-id based tenants). This is the default.
    /// </summary>
    PerRun,

    /// <summary>
    /// Every test starts its own application instance and disposes it afterwards. Fully isolated,
    /// but each test pays the application's startup cost.
    /// </summary>
    PerTest
}
