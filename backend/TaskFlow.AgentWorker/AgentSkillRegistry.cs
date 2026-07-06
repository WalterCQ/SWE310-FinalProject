using System.Diagnostics;
using System.Text;

namespace TaskFlow.AgentWorker;

public class AgentSkillRegistry(IConfiguration configuration, ILogger<AgentSkillRegistry> logger)
{
    public IReadOnlyCollection<string> RegisteredSkillIds { get; } =
    [
        "ppt-master",
        "md-to-docx",
        "openxml-docx"
    ];

    public async Task<SkillBinaryResult> BuildDeckAsync(
        string goal,
        IReadOnlyCollection<string> contextLines,
        CancellationToken cancellationToken)
    {
        return await BuildDeckFromMarkdownAsync(BuildMarkdown("TaskFlow AI Deck", goal, contextLines), cancellationToken);
    }

    public async Task<SkillBinaryResult> BuildDeckFromMarkdownAsync(
        string markdown,
        CancellationToken cancellationToken)
    {
        var skillRoot = ResolveSkillRoot("ppt-master");
        var workRoot = Path.Combine(Path.GetTempPath(), "taskflow-agent-skills", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);

        try
        {
            var inputPath = Path.Combine(workRoot, "deck.md");
            var outputPath = Path.Combine(workRoot, "taskflow-ai-deck.pptx");
            await File.WriteAllTextAsync(inputPath, markdown, Encoding.UTF8, cancellationToken);

            var adapterPath = Path.Combine(skillRoot, "taskflow_ppt_master.py");
            if (!File.Exists(adapterPath))
            {
                throw new InvalidOperationException("ppt-master adapter is missing.");
            }

            var python = configuration["AgentSkills:PythonExecutable"];
            if (string.IsNullOrWhiteSpace(python))
            {
                python = "python3";
            }

            var output = await RunProcessAsync(
                python,
                [adapterPath, inputPath, outputPath, "--workdir", Path.Combine(workRoot, "project")],
                workRoot,
                cancellationToken);

            if (!File.Exists(outputPath))
            {
                throw new InvalidOperationException("ppt-master did not produce a PPTX artifact.");
            }

            return new SkillBinaryResult(
                "ppt-master",
                "taskflow-ai-deck.pptx",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                await File.ReadAllBytesAsync(outputPath, cancellationToken),
                output);
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    public async Task<SkillBinaryResult> BuildDocxAsync(
        string goal,
        IReadOnlyCollection<string> contextLines,
        CancellationToken cancellationToken)
    {
        return await BuildDocxFromMarkdownAsync(BuildMarkdown("TaskFlow AI Report", goal, contextLines), cancellationToken);
    }

    public async Task<SkillBinaryResult> BuildDocxFromMarkdownAsync(
        string markdown,
        CancellationToken cancellationToken)
    {
        var preferred = configuration["AgentSkills:DocxSkill"];
        if (string.IsNullOrWhiteSpace(preferred)
            || string.Equals(preferred, "md-to-docx", StringComparison.OrdinalIgnoreCase))
        {
            return await BuildDocxWithMarkdownSkillAsync(markdown, cancellationToken);
        }

        throw new InvalidOperationException("Only the md-to-docx runtime skill is enabled for report artifacts.");
    }

    public string BuildMarkdown(string title, string goal, IReadOnlyCollection<string> contextLines)
    {
        var lines = new List<string>
        {
            "---",
            $"title: \"{title}\"",
            $"date: {DateTime.UtcNow:yyyy-MM-dd}",
            "version: 1.0",
            "audience: TaskFlow workspace members",
            "---",
            "",
            $"# {title}",
            "",
            "## Objective",
            goal,
            "",
            "## Source Context"
        };

        lines.AddRange(contextLines.Count == 0
            ? ["- No indexed channel or attachment context was available."]
            : contextLines.Select(line => $"- {line.Replace(Environment.NewLine, " ", StringComparison.Ordinal)}"));

        lines.AddRange([
            "",
            "## Recommended Actions",
            "- Review the generated artifact.",
            "- Approve write actions only after checking the preview.",
            "- Keep GitHub changes in a pull request for review."
        ]);

        return string.Join(Environment.NewLine, lines);
    }

    private async Task<SkillBinaryResult> BuildDocxWithMarkdownSkillAsync(
        string markdown,
        CancellationToken cancellationToken)
    {
        var skillRoot = ResolveSkillRoot("md-to-docx");
        var scriptPath = Path.Combine(skillRoot, "scripts", "md-to-docx.mjs");
        if (!File.Exists(scriptPath))
        {
            throw new InvalidOperationException("md-to-docx script is missing.");
        }

        var workRoot = Path.Combine(Path.GetTempPath(), "taskflow-agent-skills", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);
        try
        {
            var inputPath = Path.Combine(workRoot, "report.md");
            var outputPath = Path.Combine(workRoot, "taskflow-ai-report.docx");
            await File.WriteAllTextAsync(inputPath, markdown, Encoding.UTF8, cancellationToken);

            var output = await RunProcessAsync("node", [scriptPath, inputPath, outputPath], workRoot, cancellationToken);
            if (!File.Exists(outputPath))
            {
                throw new InvalidOperationException("md-to-docx did not produce a DOCX artifact.");
            }

            return new SkillBinaryResult(
                "md-to-docx",
                "taskflow-ai-report.docx",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                await File.ReadAllBytesAsync(outputPath, cancellationToken),
                output);
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    private string ResolveSkillRoot(string skillId)
    {
        var configuredRoot = configuration["AgentSkills:RootPath"];
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            candidates.Add(Path.Combine(configuredRoot, skillId));
        }

        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var i = 0; directory is not null && i < 8; i++, directory = directory.Parent)
        {
            candidates.Add(Path.Combine(directory.FullName, "tools", "agent-skills", skillId));
        }

        directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; directory is not null && i < 8; i++, directory = directory.Parent)
        {
            candidates.Add(Path.Combine(directory.FullName, "tools", "agent-skills", skillId));
        }

        var path = candidates.FirstOrDefault(Directory.Exists);
        if (path is null)
        {
            throw new InvalidOperationException($"Agent skill '{skillId}' was not found. Configure AgentSkills:RootPath.");
        }

        return path;
    }

    private static IReadOnlyCollection<string> BuildReportSections(string goal, IReadOnlyCollection<string> contextLines)
    {
        var sections = new List<string>
        {
            "Executive Summary",
            "This report was generated by TaskFlow Agent from approved workspace context.",
            "Requested Outcome",
            goal,
            "Source Context"
        };
        sections.AddRange(contextLines.Count == 0 ? ["No indexed channel or attachment context was available."] : contextLines);
        sections.Add("Recommended Next Steps");
        sections.Add("Review artifacts and approve write actions only after checking their previews.");
        return sections;
    }

    private async Task<string> RunProcessAsync(
        string fileName,
        IReadOnlyCollection<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        logger.LogInformation("Running agent skill command {FileName} {Arguments}", fileName, string.Join(" ", arguments));
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start process: {fileName}");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} failed with exit code {process.ExitCode}: {Shorten(output + Environment.NewLine + error, 2000)}");
        }

        return string.IsNullOrWhiteSpace(output) ? error : output;
    }

    private static string Shorten(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "...";
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Temporary skill work directories are best-effort cleanup.
        }
    }
}

public sealed record SkillBinaryResult(
    string SkillId,
    string FileName,
    string ContentType,
    byte[] Content,
    string ExecutionLog);
