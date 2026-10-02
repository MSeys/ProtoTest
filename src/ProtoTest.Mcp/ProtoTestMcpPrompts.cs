namespace ProtoTest.Mcp;

using System.ComponentModel;
using ModelContextProtocol.Server;

/// <summary>
/// The three jobs a coding agent does with ProtoTest, as prompts any MCP client can offer: fix a failing
/// test, cover a change, improve weak tests. Each one starts from the suite map, works from recorded
/// evidence and ends with the tool call that judges the result, so the agent cannot call it done on its
/// own say-so.
/// </summary>
[McpServerPromptType]
public sealed class ProtoTestMcpPrompts
{
    /// <summary>Fix a failing test until the receipt proves it.</summary>
    [McpServerPrompt(Name = "fix_failure")]
    [Description("Fix a failing ProtoTest test from its recorded evidence and finish only when check_fix proves the fix.")]
    public static string FixFailure(
        [Description("The failing test's name; defaults to the newest run's first failing test.")]
        string? test = null)
    {
        var which = string.IsNullOrWhiteSpace(test) ? "the newest run's first failing test" : $"'{test}'";
        return $"""
            Fix {which} in this ProtoTest suite. Work from the recorded evidence; do not guess a cause.

            1. Call get_suite_map once and follow the suite's style: its clients, data provisioners, attributes and page objects.
            2. Call get_failure{(string.IsNullOrWhiteSpace(test) ? string.Empty : $" with testId '{test}'")}. When the cause is not plain, call get_diagnosis with detail=context.
            3. When an earlier run of this test passed, call compare_runs: it names the operation where this run left the green one.
            4. Decide what is wrong. When the test states the intended behaviour, change the code. Change the test only when the evidence shows its expectation is wrong, and say which evidence.
            5. Rerun the failing test, then call check_fix. The fix is done only when check_fix says proven. For every reason it lists, address it and rerun.
            6. Report the receipt: the operation that changed, the tests it proves, and that no other test broke.
            """;
    }

    /// <summary>Cover a change with tests that reach what it added and can fail.</summary>
    [McpServerPrompt(Name = "cover_change")]
    [Description("Cover a change with ProtoTest integration tests in the suite's style, guided by the uncovered units and their suggestions.")]
    public static string CoverChange(
        [Description("What changed, for example 'the new DELETE /api/projects/{id} endpoint'.")]
        string change,
        [Description("Only units of this target, for example 'Shop:Api'.")]
        string? target = null)
    {
        var filter = string.IsNullOrWhiteSpace(target) ? string.Empty : $" with target '{target}'";
        return $"""
            Cover this change with integration tests: {change}

            1. Call get_suite_map. Reuse its clients, provisioners, attributes and page objects, and shape the test like its example for this kind of work.
            2. Call get_coverage{filter}. Each uncovered unit has a suggestion: extend the named test when it already calls the endpoint, or write a new test shaped like the named one.
            3. Write or extend the test. Assert on the answer (status, shape, message), and add the failure path the change introduces.
            4. Run the test, then call get_coverage again: the units the change added must read covered.
            5. Call review_tests for the new or changed tests: each must be clean (it checks what it calls and records its waits).
            6. Break the expectation once on purpose and run the test: it must fail. Restore it. A test that cannot fail proves nothing.
            """;
    }

    /// <summary>Improve the tests a review flags until they read clean.</summary>
    [McpServerPrompt(Name = "improve_tests")]
    [Description("Improve weak ProtoTest tests: apply each review finding's next step and finish when review_tests reads clean and compare_runs shows nothing broke.")]
    public static string ImproveTests(
        [Description("The test to improve; defaults to every test review_tests flags.")]
        string? test = null)
    {
        var which = string.IsNullOrWhiteSpace(test) ? "the tests review_tests flags" : $"'{test}'";
        return $"""
            Improve {which} in this ProtoTest suite.

            1. Call review_tests{(string.IsNullOrWhiteSpace(test) ? string.Empty : $" with tests ['{test}']")}. Each finding names a rule (no-check, unchecked-call, untraced-gap) and its next step.
            2. Call get_suite_map and make each change in the suite's style.
            3. Apply each finding's next step: assert on what the test is about, check or move unchecked calls, replace sleeps with waits that record what they wait for.
            4. Rerun the tests, then call review_tests again: they must be clean.
            5. Call compare_runs against the run before your change: no test may read broken.
            """;
    }
}
