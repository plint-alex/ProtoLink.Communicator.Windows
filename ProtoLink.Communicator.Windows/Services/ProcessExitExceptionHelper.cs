namespace ProtoLink.Communicator.Windows.Services;

/// <summary>
/// Classifies known-benign native/CRT unload failures that fire on AppDomain/process exit
/// (WPF C++/CLI modules such as DirectWriteForwarder, System.Printing).
/// </summary>
internal static class ProcessExitExceptionHelper
{
    /// <summary>
    /// True when the exception is a process-terminating CRT/module unload failure that must not
    /// be shown as a user-facing "Background error" dialog — the UI is already shutting down.
    /// </summary>
    public static bool IsBenignNativeUnloadDuringProcessExit(Exception? ex, bool isTerminating)
    {
        if (!isTerminating || ex == null)
            return false;

        if (ex is AggregateException agg)
        {
            foreach (var inner in agg.Flatten().InnerExceptions)
            {
                if (IsNativeUnloadStack(inner))
                    return true;
            }
            return false;
        }

        return IsNativeUnloadStack(ex);
    }

    private static bool IsNativeUnloadStack(Exception ex)
    {
        var stack = ex.StackTrace ?? string.Empty;
        if (stack.Length == 0 && ex.InnerException != null)
            stack = ex.InnerException.StackTrace ?? string.Empty;

        if (Contains(stack, "ModuleUninitializer") ||
            Contains(stack, "CrtImplementationDetails") ||
            Contains(stack, "__std_type_info_destroy_list") ||
            Contains(stack, "__scrt_uninitialize_type_info") ||
            Contains(stack, "SingletonDomainUnload") ||
            Contains(stack, "LanguageSupport.DomainUnload"))
            return true;

        // Empty "Dll was not found." with no useful stack still happens on exit for mixed modules.
        if (ex is DllNotFoundException &&
            (string.IsNullOrWhiteSpace(ex.Message) ||
             Contains(ex.Message, "Dll was not found")))
            return true;

        return false;
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
