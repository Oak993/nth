These are Marrow-authored synthetic stream-json transcripts, never captured from a live CLI.
Fields follow ClaudeSession.cs Handle, HandleAssistant, HandleToolResults and HandleControlRequest
(B3 ClaudeSession.swift:330-384, 503-560, 609-675) and the documented stream-json formats
cited in skills-research-loading.md section 3.1 and skills-ONDEMAND-PLAN.md sections 5.1-5.2.
Partial tool blocks have no complete input; the completed assistant block provides it.
Claude tool_result supplies is_error and content, not an exit code. Duplicate/unknown results
must not fabricate a command. Skill input uses the provider's existing skill/command fields.
