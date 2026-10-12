using System.CommandLine;
using Governance.Auditor.Common;
using Governance.Auditor.Session;

namespace Governance.Auditor.Cli;

internal static class SessionCommand
{
    public static Command Create(CliContext context)
    {
        var moment = new Option<string>("--moment") { Description = "Whether the session is starting or ending: start or end.", Required = true };
        moment.AcceptOnlyFromAmong([.. SessionVocabulary.Moments]);

        var task = new Option<string>("--task")
        {
            Description = "The kind of task that comes next: work-package, pull-request (follow-up on the open pull request), or bug-fix.",
            DefaultValueFactory = _ => SessionVocabulary.WorkPackage,
        };
        task.AcceptOnlyFromAmong([.. SessionVocabulary.Tasks]);

        var contextOption = new Option<string>("--context")
        {
            Description = "What the conversation of this session is like: fresh (it began with the kickoff prompt), used, or compacted (it was summarized, so detail is lost). Only the caller knows.",
            DefaultValueFactory = _ => SessionVocabulary.Used,
        };
        contextOption.AcceptOnlyFromAmong([.. SessionVocabulary.Contexts]);

        var json = new Option<bool>("--json") { Description = "Print the result as one line of JSON instead of text." };
        var summary = StatusCommands.SummaryOption();

        var command = new Command(
            "session",
            "Say whether the next task belongs in a fresh session or in this one, name the handoff gaps to close first, and print the kickoff prompt. Reads status.yaml and the local Git state only.")
        {
            moment, task, contextOption, json, summary,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var files = context.Files(parseResult);
            var policy = SessionPolicy.Load(files);
            var request = new SessionRequest(parseResult.GetValue(moment)!, parseResult.GetValue(task)!, parseResult.GetValue(contextOption)!);

            var advice = await SessionAdvisor.AdviseAsync(files, context.Processes, policy, request, cancellationToken);

            context.Output.WriteLine(parseResult.GetValue(json) ? SessionReport.ToJson(advice) : SessionReport.RenderText(advice));

            if (parseResult.GetValue(summary) is { Length: > 0 } summaryPath)
            {
                MarkdownText.Append(summaryPath, SessionReport.RenderMarkdown(advice));
            }

            // Advice is not a pass or a fail, except that an open handoff or a previous session's leftovers must stop the session.
            return advice.ExitCode;
        });

        return command;
    }
}
