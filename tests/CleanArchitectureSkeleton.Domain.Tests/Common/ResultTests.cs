using CleanArchitectureSkeleton.Domain.Common;

namespace CleanArchitectureSkeleton.Domain.Tests.Common;

/// <summary>Tests unitaires purs : aucun mock, aucune base, aucune I/O. Ils s'exécutent en quelques millisecondes.</summary>
public class ResultTests
{
    private static readonly Error SomeError = Error.NotFound("Test.NotFound", "introuvable");

    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_carries_its_error()
    {
        var result = Result.Failure(SomeError);

        Assert.True(result.IsFailure);
        Assert.Equal(SomeError, result.Error);
    }

    [Fact]
    public void Failure_with_Error_None_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => Result.Failure(Error.None));
    }

    [Fact]
    public void Generic_success_exposes_value()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Reading_value_of_a_failure_is_a_programming_error()
    {
        var result = Result.Failure<int>(SomeError);

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Implicit_conversions_build_results()
    {
        Result<string> success = "ok";
        Result<string> failure = SomeError;

        Assert.True(success.IsSuccess);
        Assert.Equal("ok", success.Value);
        Assert.True(failure.IsFailure);
        Assert.Equal(SomeError, failure.Error);
    }

    [Fact]
    public void Map_transforms_success_and_propagates_failure()
    {
        Result<int> success = 2;
        Result<int> failure = SomeError;

        Assert.Equal(4, success.Map(x => x * 2).Value);
        Assert.Equal(SomeError, failure.Map(x => x * 2).Error);
    }

    [Fact]
    public void Match_runs_the_matching_branch_only()
    {
        Result<int> success = 1;
        Result<int> failure = SomeError;

        Assert.Equal("ok:1", success.Match(v => $"ok:{v}", e => $"ko:{e.Code}"));
        Assert.Equal("ko:Test.NotFound", failure.Match(v => $"ok:{v}", e => $"ko:{e.Code}"));
    }
}
