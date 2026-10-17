using ProtoLink.Communicator.Windows.Services;
using Xunit;

namespace ProtoLink.Communicator.Windows.IntegrationTests;

/// <summary>Exit-time DllNotFoundException from WPF C++/CLI must not surface as Background error.</summary>
[Trait("Category", "Offline")]
public class ProcessExitExceptionHelperTests
{
    [Fact]
    public void Ignores_DllNotFound_WithModuleUninitializerStack_WhenTerminating()
    {
        var withStack = new ExitStackException(
            "Dll was not found.",
            """
            at __std_type_info_destroy_list(__type_info_node*)
            at __scrt_uninitialize_type_info()
            at _app_exit_callback()
            at <CrtImplementationDetails>.ModuleUninitializer.SingletonDomainUnload(Object source, EventArgs arguments)
            """);

        Assert.True(ProcessExitExceptionHelper.IsBenignNativeUnloadDuringProcessExit(withStack, isTerminating: true));
    }

    [Fact]
    public void Ignores_EmptyDllNotFound_WhenTerminating()
    {
        var ex = new DllNotFoundException("Dll was not found.");
        Assert.True(ProcessExitExceptionHelper.IsBenignNativeUnloadDuringProcessExit(ex, isTerminating: true));
    }

    [Fact]
    public void DoesNotIgnore_WhenNotTerminating()
    {
        var ex = new DllNotFoundException("Dll was not found.");
        Assert.False(ProcessExitExceptionHelper.IsBenignNativeUnloadDuringProcessExit(ex, isTerminating: false));
    }

    [Fact]
    public void DoesNotIgnore_UnrelatedException_WhenTerminating()
    {
        var ex = new InvalidOperationException("something else");
        Assert.False(ProcessExitExceptionHelper.IsBenignNativeUnloadDuringProcessExit(ex, isTerminating: true));
    }

    private sealed class ExitStackException : DllNotFoundException
    {
        private readonly string _stack;

        public ExitStackException(string message, string stack) : base(message) => _stack = stack;

        public override string? StackTrace => _stack;
    }
}
