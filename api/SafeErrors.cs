namespace CareerCoach;

/// <summary>
/// Helpers for returning errors to API clients without leaking internal details.
///
/// Why this exists: a Npgsql connection failure (for example) throws an exception whose
/// .Message includes the database host and port — "Failed to connect to 1.2.3.4:25060".
/// If we hand that straight to the client via Results.Problem(detail: ex.Message, ...),
/// any browser DevTools session can read it. Same problem with stack traces, dependency
/// versions, file paths, and other infrastructure details.
///
/// Use <see cref="ServerError"/> in catch handlers instead. It logs the full exception
/// server-side (so you can still debug from logs) and returns a sanitized 500 response.
/// </summary>
public static class SafeErrors
{
    /// <summary>
    /// Logs <paramref name="ex"/> with the given category and returns a 500 Problem
    /// result whose body contains only the supplied user-facing message.
    /// </summary>
    public static IResult ServerError(
        Exception ex,
        string category,
        string userMessage = "Something went wrong on our end. Please try again in a moment.")
    {
        Console.WriteLine($"[{category}] {ex.GetType().Name}: {ex.Message}");
        if (ex.InnerException != null)
            Console.WriteLine($"[{category}] Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        if (!string.IsNullOrEmpty(ex.StackTrace))
            Console.WriteLine($"[{category}] Stack: {ex.StackTrace}");

        return Results.Problem(
            detail: userMessage,
            statusCode: 500,
            title: "Internal server error"
        );
    }
}
