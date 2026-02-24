// Argha - 2026-02-24 - five built-in prompt templates that leverage all 9 registered tools
using McpServer.Protocol;

namespace McpServer.Prompts;

/// <summary>
/// Provides five reusable, parameterized prompt templates covering the main tool workflows.
/// </summary>
public class BuiltInPromptProvider : IPromptProvider
{
    private static readonly IReadOnlyList<Prompt> _prompts = new List<Prompt>
    {
        new Prompt
        {
            Name = "summarize_file",
            Description = "Read a file and return a concise summary.",
            Arguments = new List<PromptArgument>
            {
                new PromptArgument { Name = "path", Description = "Absolute path to the file", Required = true }
            }
        },
        new Prompt
        {
            Name = "sql_query_helper",
            Description = "Write and run a SQL query from a plain-English question.",
            Arguments = new List<PromptArgument>
            {
                new PromptArgument { Name = "database", Description = "Named SQL connection to query", Required = true },
                new PromptArgument { Name = "question", Description = "Plain-English question to answer with SQL", Required = true }
            }
        },
        new Prompt
        {
            Name = "git_diff_review",
            Description = "Show recent commits and uncommitted changes in a repository.",
            Arguments = new List<PromptArgument>
            {
                new PromptArgument { Name = "repository", Description = "Absolute path to the git repository", Required = true }
            }
        },
        new Prompt
        {
            Name = "http_api_call",
            Description = "Fetch a URL and analyze the response.",
            Arguments = new List<PromptArgument>
            {
                new PromptArgument { Name = "url", Description = "URL to fetch", Required = true },
                new PromptArgument { Name = "goal", Description = "What to extract or analyze from the response", Required = false }
            }
        },
        new Prompt
        {
            Name = "explain_code",
            Description = "Read a source file and explain what the code does.",
            Arguments = new List<PromptArgument>
            {
                new PromptArgument { Name = "path", Description = "Absolute path to the source file", Required = true },
                new PromptArgument { Name = "language", Description = "Programming language (for context)", Required = false }
            }
        }
    };

    public bool CanHandle(string name) =>
        _prompts.Any(p => p.Name == name);

    public Task<IEnumerable<Prompt>> ListPromptsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IEnumerable<Prompt>>(_prompts);

    public Task<GetPromptResult> GetPromptAsync(
        string name,
        Dictionary<string, string>? arguments,
        CancellationToken cancellationToken)
    {
        var prompt = _prompts.FirstOrDefault(p => p.Name == name)
            ?? throw new ArgumentException($"Prompt not found: {name}", nameof(name));

        // Argha - 2026-02-24 - validate required arguments before rendering
        foreach (var arg in prompt.Arguments.Where(a => a.Required))
        {
            if (arguments == null || !arguments.TryGetValue(arg.Name, out var val) || string.IsNullOrEmpty(val))
                throw new ArgumentException($"Missing required argument: {arg.Name}", nameof(arguments));
        }

        var text = name switch
        {
            "summarize_file" => RenderSummarizeFile(arguments!),
            "sql_query_helper" => RenderSqlQueryHelper(arguments!),
            "git_diff_review" => RenderGitDiffReview(arguments!),
            "http_api_call" => RenderHttpApiCall(arguments!),
            "explain_code" => RenderExplainCode(arguments!),
            _ => throw new ArgumentException($"Prompt not found: {name}", nameof(name))
        };

        var result = new GetPromptResult
        {
            Description = prompt.Description,
            Messages = new List<PromptMessage>
            {
                new PromptMessage
                {
                    Role = "user",
                    Content = new PromptMessageContent { Type = "text", Text = text }
                }
            }
        };

        return Task.FromResult(result);
    }

    // Argha - 2026-02-24 - template renderers — one per prompt name

    private static string RenderSummarizeFile(Dictionary<string, string> args)
    {
        var path = args["path"];
        return $"Please read and summarize the file at: {path}\n\n" +
               "Use the filesystem tool to read the file, then provide a concise summary of its contents, " +
               "including the main purpose, key sections, and any important details.";
    }

    private static string RenderSqlQueryHelper(Dictionary<string, string> args)
    {
        var database = args["database"];
        var question = args["question"];
        return $"Using the '{database}' database connection, answer the following question:\n\n{question}\n\n" +
               "Use the sql_query tool to write and execute an appropriate SELECT query. " +
               "Show the query you used and explain the results.";
    }

    private static string RenderGitDiffReview(Dictionary<string, string> args)
    {
        var repository = args["repository"];
        return $"Review the current state of the git repository at: {repository}\n\n" +
               "Use the git tool to:\n" +
               "1. Show the last 5 commits (git log)\n" +
               "2. Show uncommitted changes (git status and git diff)\n" +
               "Then provide a summary of what has changed and any observations about the work in progress.";
    }

    private static string RenderHttpApiCall(Dictionary<string, string> args)
    {
        var url = args["url"];
        args.TryGetValue("goal", out var goal);
        var goalClause = string.IsNullOrEmpty(goal)
            ? "Summarize the response and highlight key information."
            : goal;
        return $"Fetch the following URL using the http_request tool: {url}\n\n{goalClause}";
    }

    private static string RenderExplainCode(Dictionary<string, string> args)
    {
        var path = args["path"];
        args.TryGetValue("language", out var language);
        var langClause = string.IsNullOrEmpty(language) ? "" : $" ({language})";
        return $"Please read and explain the code{langClause} at: {path}\n\n" +
               "Use the filesystem tool to read the file, then explain:\n" +
               "1. What the code does at a high level\n" +
               "2. Key functions, classes, or structures\n" +
               "3. Any notable patterns or design decisions\n" +
               "4. Potential issues or areas for improvement";
    }
}
