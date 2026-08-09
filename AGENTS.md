# Agent Rules

## General Agent Task Execution

When invoking the `@general` subagent:
- The subagent should complete all tasks internally within its own execution context.
- Do not expose intermediate steps, detailed tool calls, or partial results to the user.
- Upon completion, the subagent should report only a concise summary of the work done.
- The main assistant should relay this summary to the user without additional elaboration.
