using System.Threading.Tasks;
using CallgraphClosure.Core;
using Xunit;

namespace MustNotBlock.Tests;

public class MustNotBlockTests
{
    [Fact]
    public async Task AnnotatedMethod_DirectThreadSleep_FiresCGC003()
    {
        var source = """
            using System.Threading;
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotBlock]
                void F() { Thread.Sleep(100); }
            }
            """;

        var sinkHit = CSharpAnalyzerVerifier<MustNotBlockAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(7, 16)
            .WithArguments("F", "MustNotBlockAttribute", "Thread.Sleep");

        // External call to Thread.Sleep also fires CGC002.
        var externalCall = CSharpAnalyzerVerifier<MustNotBlockAnalyzer>
            .Diagnostic(Diagnostics.ExternalBoundary)
            .WithLocation(7, 16)
            .WithArguments("F", "MustNotBlockAttribute", "Sleep");

        await CSharpAnalyzerVerifier<MustNotBlockAnalyzer>
            .VerifyAnalyzerAsync(source, sinkHit, externalCall);
    }

    [Fact]
    public async Task AnnotatedMethod_DirectTaskWait_FiresCGC003()
    {
        var source = """
            using System.Threading.Tasks;
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotBlock]
                void F(Task t) { t.Wait(); }
            }
            """;

        var sinkHit = CSharpAnalyzerVerifier<MustNotBlockAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(7, 22)
            .WithArguments("F", "MustNotBlockAttribute", "Task.Wait");

        var externalCall = CSharpAnalyzerVerifier<MustNotBlockAnalyzer>
            .Diagnostic(Diagnostics.ExternalBoundary)
            .WithLocation(7, 22)
            .WithArguments("F", "MustNotBlockAttribute", "Wait");

        await CSharpAnalyzerVerifier<MustNotBlockAnalyzer>
            .VerifyAnalyzerAsync(source, sinkHit, externalCall);
    }

    [Fact]
    public async Task AnnotatedMethod_TaskTResult_FiresCGC003()
    {
        var source = """
            using System.Threading.Tasks;
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotBlock]
                int F(Task<int> t) { return t.Result; }
            }
            """;

        var sinkHit = CSharpAnalyzerVerifier<MustNotBlockAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(7, 33)
            .WithArguments("F", "MustNotBlockAttribute", "Task.Result");

        await CSharpAnalyzerVerifier<MustNotBlockAnalyzer>
            .VerifyAnalyzerAsync(source, sinkHit);
    }

    [Fact]
    public async Task AnnotatedMethod_SemaphoreSlimWait_FiresCGC003()
    {
        var source = """
            using System.Threading;
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotBlock]
                void F(SemaphoreSlim s) { s.Wait(); }
            }
            """;

        var sinkHit = CSharpAnalyzerVerifier<MustNotBlockAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(7, 31)
            .WithArguments("F", "MustNotBlockAttribute", "SemaphoreSlim.Wait");

        var externalCall = CSharpAnalyzerVerifier<MustNotBlockAnalyzer>
            .Diagnostic(Diagnostics.ExternalBoundary)
            .WithLocation(7, 31)
            .WithArguments("F", "MustNotBlockAttribute", "Wait");

        await CSharpAnalyzerVerifier<MustNotBlockAnalyzer>
            .VerifyAnalyzerAsync(source, sinkHit, externalCall);
    }

    [Fact]
    public async Task AnnotatedMethod_SemaphoreSlimWaitAsync_NoCGC003()
    {
        // WaitAsync is the async overload — must not match the blocking sink.
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotBlock]
                async Task F(SemaphoreSlim s) { await s.WaitAsync(); }
            }
            """;

        // The external call to WaitAsync still fires CGC002 (info-level boundary).
        var externalCall = CSharpAnalyzerVerifier<MustNotBlockAnalyzer>
            .Diagnostic(Diagnostics.ExternalBoundary)
            .WithLocation(8, 43)
            .WithArguments("F", "MustNotBlockAttribute", "WaitAsync");

        await CSharpAnalyzerVerifier<MustNotBlockAnalyzer>
            .VerifyAnalyzerAsync(source, externalCall);
    }

    [Fact]
    public async Task AnnotatedMethod_NoBlocking_Silent()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotBlock]
                void F() { }
            }
            """;

        await CSharpAnalyzerVerifier<MustNotBlockAnalyzer>
            .VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task AnnotatedMethod_CallsUnannotatedMethod_FiresCGC001()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotBlock]
                void F() { Helper(); }

                void Helper() { }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotBlockAnalyzer>
            .Diagnostic(Diagnostics.SourceBoundary)
            .WithLocation(6, 16)
            .WithArguments("F", "MustNotBlockAttribute", "Helper");

        await CSharpAnalyzerVerifier<MustNotBlockAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task AnnotatedMethod_CallsAnnotatedMethod_Silent()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotBlock]
                void F() { Helper(); }

                [MustNotBlock]
                void Helper() { }
            }
            """;

        await CSharpAnalyzerVerifier<MustNotBlockAnalyzer>
            .VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task AnnotatedAsyncMethod_AwaitsTaskYield_NoCGC003()
    {
        // Canary: compiler-generated state machine plumbing must not produce false CGC003s.
        var source = """
            using System.Threading.Tasks;
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotBlock]
                async Task F() { await Task.Yield(); }
            }
            """;

        // External Task.Yield call surfaces as CGC002.
        var externalCall = CSharpAnalyzerVerifier<MustNotBlockAnalyzer>
            .Diagnostic(Diagnostics.ExternalBoundary)
            .WithLocation(7, 28)
            .WithArguments("F", "MustNotBlockAttribute", "Yield");

        await CSharpAnalyzerVerifier<MustNotBlockAnalyzer>
            .VerifyAnalyzerAsync(source, externalCall);
    }
}
