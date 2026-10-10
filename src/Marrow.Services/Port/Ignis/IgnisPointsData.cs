// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-engine; PLAN unit U1). The model points compiled into Marrow (the .csproj is
// protected, so the file is a C# const string, like ModelPriceData.cs). Every price is US dollars per million tokens as
// printed on the official page named by the model's sourceUrl on its retrievedAt day (Anthropic: the Claude pricing page
// and per-model overview pages; OpenAI: the API pricing page); none is estimated. Claude cache lifetimes: main chat 1 h,
// sub-agents 5 min (PLAN lead decision; Anthropic documents both TTLs). OpenAI documents a 30-minute cache for GPT-5.6
// and later (ttlMainSeconds 1800) and none fixed for earlier models (left out). Codex keep-warm is off (PLAN lead
// decision). OpenAI's cache-write price for GPT-5.6 and later (1.25x input, 30 min) has no field in schema v1 (it names
// only 5-minute and 1-hour writes), so it is left out; Codex reports no write tokens, so no cost is computed from it.
namespace Marrow.Services.Port.Ignis;

public static class IgnisPointsData
{
    public const string Json = """
        {
          "schemaVersion": 1,
          "version": "2026.10.10",
          "models": [
            {
              "provider": "claude",
              "ids": [
                "claude-opus-5-5"
              ],
              "aliases": [
                "opus"
              ],
              "displayName": "Opus 5.5",
              "contextWindow": 1000000,
              "prices": {
                "input": 4,
                "output": 20,
                "cacheRead": 0.2,
                "cacheWrite5m": 5,
                "cacheWrite1h": 8,
                "steps": []
              },
              "cache": {
                "ttlMainSeconds": 3600,
                "ttlSubagentSeconds": 300,
                "keepWarm": true
              },
              "helperModel": "claude-haiku-5-5",
              "sourceUrl": "https://platform.claude.com/docs/en/about-claude/pricing",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "claude",
              "ids": [
                "claude-sonnet-5-5"
              ],
              "aliases": [
                "sonnet"
              ],
              "displayName": "Sonnet 5.5",
              "contextWindow": 1000000,
              "prices": {
                "input": 2,
                "output": 10,
                "cacheRead": 0.1,
                "cacheWrite5m": 2.5,
                "cacheWrite1h": 4,
                "steps": []
              },
              "cache": {
                "ttlMainSeconds": 3600,
                "ttlSubagentSeconds": 300,
                "keepWarm": true
              },
              "helperModel": "claude-haiku-5-5",
              "sourceUrl": "https://platform.claude.com/docs/en/about-claude/pricing",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "claude",
              "ids": [
                "claude-haiku-5-5"
              ],
              "aliases": [
                "haiku"
              ],
              "displayName": "Haiku 5.5",
              "contextWindow": 1000000,
              "prices": {
                "input": 0.1,
                "output": 0.5,
                "cacheRead": 0.01,
                "cacheWrite5m": 0.125,
                "cacheWrite1h": 0.2,
                "steps": [
                  {
                    "aboveTokens": 100000,
                    "input": 0.5,
                    "output": 2.5,
                    "cacheRead": 0.05,
                    "cacheWrite5m": 0.625,
                    "cacheWrite1h": 1
                  }
                ]
              },
              "cache": {
                "ttlMainSeconds": 3600,
                "ttlSubagentSeconds": 300,
                "keepWarm": true
              },
              "sourceUrl": "https://platform.claude.com/docs/en/about-claude/pricing",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "claude",
              "ids": [
                "claude-fable-5-1"
              ],
              "aliases": [
                "fable"
              ],
              "displayName": "Fable 5.1",
              "contextWindow": 1000000,
              "prices": {
                "input": 10,
                "output": 50,
                "cacheRead": 0.25,
                "cacheWrite5m": 12.5,
                "cacheWrite1h": 20,
                "steps": []
              },
              "cache": {
                "ttlMainSeconds": 3600,
                "ttlSubagentSeconds": 300,
                "keepWarm": true
              },
              "helperModel": "claude-haiku-5-5",
              "sourceUrl": "https://platform.claude.com/docs/en/about-claude/pricing",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "claude",
              "ids": [
                "claude-opus-5"
              ],
              "aliases": [],
              "displayName": "Opus 5",
              "contextWindow": 1000000,
              "prices": {
                "input": 5,
                "output": 25,
                "cacheRead": 0.5,
                "cacheWrite5m": 6.25,
                "cacheWrite1h": 10,
                "steps": []
              },
              "cache": {
                "ttlMainSeconds": 3600,
                "ttlSubagentSeconds": 300,
                "keepWarm": true
              },
              "helperModel": "claude-haiku-5-5",
              "sourceUrl": "https://platform.claude.com/docs/en/models/opus-5/overview",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "claude",
              "ids": [
                "claude-sonnet-5"
              ],
              "aliases": [],
              "displayName": "Sonnet 5",
              "contextWindow": 1000000,
              "prices": {
                "input": 2,
                "output": 10,
                "cacheRead": 0.2,
                "cacheWrite5m": 2.5,
                "cacheWrite1h": 4,
                "steps": []
              },
              "cache": {
                "ttlMainSeconds": 3600,
                "ttlSubagentSeconds": 300,
                "keepWarm": true
              },
              "helperModel": "claude-haiku-5-5",
              "sourceUrl": "https://platform.claude.com/docs/en/models/sonnet-5/overview",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "claude",
              "ids": [
                "claude-haiku-4-5-20251001"
              ],
              "aliases": [
                "claude-haiku-4-5"
              ],
              "displayName": "Haiku 4.5",
              "contextWindow": 200000,
              "prices": {
                "input": 1,
                "output": 5,
                "cacheRead": 0.1,
                "cacheWrite5m": 1.25,
                "cacheWrite1h": 2,
                "steps": []
              },
              "cache": {
                "ttlMainSeconds": 3600,
                "ttlSubagentSeconds": 300,
                "keepWarm": true
              },
              "sourceUrl": "https://platform.claude.com/docs/en/models/haiku-4-5/overview",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "codex",
              "ids": [
                "gpt-6-astra"
              ],
              "aliases": [],
              "displayName": "GPT-6 Astra",
              "contextWindow": 1050000,
              "prices": {
                "input": 10,
                "output": 50,
                "cacheRead": 1.0,
                "steps": [
                  {
                    "aboveTokens": 272000,
                    "input": 20,
                    "output": 75,
                    "cacheRead": 2.0
                  }
                ]
              },
              "cache": {
                "ttlMainSeconds": 1800,
                "keepWarm": false
              },
              "sourceUrl": "https://developers.openai.com/api/docs/pricing",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "codex",
              "ids": [
                "gpt-6.1-sol"
              ],
              "aliases": [],
              "displayName": "GPT-6.1 Sol",
              "contextWindow": 1050000,
              "prices": {
                "input": 2,
                "output": 10,
                "cacheRead": 0.1,
                "steps": [
                  {
                    "aboveTokens": 272000,
                    "input": 4,
                    "output": 15,
                    "cacheRead": 0.2
                  }
                ]
              },
              "cache": {
                "ttlMainSeconds": 1800,
                "keepWarm": false
              },
              "sourceUrl": "https://developers.openai.com/api/docs/pricing",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "codex",
              "ids": [
                "gpt-6-sol"
              ],
              "aliases": [],
              "displayName": "GPT-6 Sol",
              "contextWindow": 1050000,
              "prices": {
                "input": 2,
                "output": 10,
                "cacheRead": 0.2,
                "steps": [
                  {
                    "aboveTokens": 272000,
                    "input": 4,
                    "output": 15,
                    "cacheRead": 0.4
                  }
                ]
              },
              "cache": {
                "ttlMainSeconds": 1800,
                "keepWarm": false
              },
              "sourceUrl": "https://developers.openai.com/api/docs/pricing",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "codex",
              "ids": [
                "gpt-6-luna"
              ],
              "aliases": [],
              "displayName": "GPT-6 Luna",
              "contextWindow": 1050000,
              "prices": {
                "input": 0.1,
                "output": 0.5,
                "cacheRead": 0.01,
                "steps": [
                  {
                    "aboveTokens": 272000,
                    "input": 0.2,
                    "output": 0.75,
                    "cacheRead": 0.02
                  }
                ]
              },
              "cache": {
                "ttlMainSeconds": 1800,
                "keepWarm": false
              },
              "sourceUrl": "https://developers.openai.com/api/docs/pricing",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "codex",
              "ids": [
                "gpt-5.6-sol"
              ],
              "aliases": [],
              "displayName": "GPT-5.6 Sol",
              "contextWindow": 1050000,
              "prices": {
                "input": 4,
                "output": 20,
                "cacheRead": 0.4,
                "steps": [
                  {
                    "aboveTokens": 272000,
                    "input": 8,
                    "output": 30,
                    "cacheRead": 0.8
                  }
                ]
              },
              "cache": {
                "ttlMainSeconds": 1800,
                "keepWarm": false
              },
              "sourceUrl": "https://developers.openai.com/api/docs/pricing",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "codex",
              "ids": [
                "gpt-5.6-terra"
              ],
              "aliases": [],
              "displayName": "GPT-5.6 Terra",
              "contextWindow": 1050000,
              "prices": {
                "input": 2,
                "output": 12,
                "cacheRead": 0.2,
                "steps": [
                  {
                    "aboveTokens": 272000,
                    "input": 4,
                    "output": 18,
                    "cacheRead": 0.4
                  }
                ]
              },
              "cache": {
                "ttlMainSeconds": 1800,
                "keepWarm": false
              },
              "sourceUrl": "https://developers.openai.com/api/docs/pricing",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "codex",
              "ids": [
                "gpt-5.6-luna"
              ],
              "aliases": [],
              "displayName": "GPT-5.6 Luna",
              "contextWindow": 1050000,
              "prices": {
                "input": 0.2,
                "output": 1.2,
                "cacheRead": 0.02,
                "steps": [
                  {
                    "aboveTokens": 272000,
                    "input": 0.4,
                    "output": 1.8,
                    "cacheRead": 0.04
                  }
                ]
              },
              "cache": {
                "ttlMainSeconds": 1800,
                "keepWarm": false
              },
              "sourceUrl": "https://developers.openai.com/api/docs/pricing",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "codex",
              "ids": [
                "gpt-5.5",
                "gpt-5.5-2026-04-23"
              ],
              "aliases": [],
              "displayName": "GPT-5.5",
              "contextWindow": 1050000,
              "prices": {
                "input": 5,
                "output": 30,
                "cacheRead": 0.5,
                "steps": [
                  {
                    "aboveTokens": 272000,
                    "input": 10,
                    "output": 45,
                    "cacheRead": 1.0
                  }
                ]
              },
              "cache": {
                "keepWarm": false
              },
              "sourceUrl": "https://developers.openai.com/api/docs/pricing",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "codex",
              "ids": [
                "gpt-5.4",
                "gpt-5.4-2026-03-05"
              ],
              "aliases": [],
              "displayName": "GPT-5.4",
              "contextWindow": 1050000,
              "prices": {
                "input": 2.5,
                "output": 15,
                "cacheRead": 0.25,
                "steps": [
                  {
                    "aboveTokens": 272000,
                    "input": 5,
                    "output": 22.5,
                    "cacheRead": 0.5
                  }
                ]
              },
              "cache": {
                "keepWarm": false
              },
              "sourceUrl": "https://developers.openai.com/api/docs/pricing",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "codex",
              "ids": [
                "gpt-5.3-codex"
              ],
              "aliases": [],
              "displayName": "GPT-5.3 Codex",
              "contextWindow": 400000,
              "prices": {
                "input": 1.75,
                "output": 14,
                "cacheRead": 0.175,
                "steps": []
              },
              "cache": {
                "keepWarm": false
              },
              "sourceUrl": "https://developers.openai.com/api/docs/pricing",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "codex",
              "ids": [
                "gpt-5.4-mini",
                "gpt-5.4-mini-2026-03-17"
              ],
              "aliases": [],
              "displayName": "GPT-5.4 mini",
              "contextWindow": 400000,
              "prices": {
                "input": 0.75,
                "output": 4.5,
                "cacheRead": 0.075,
                "steps": []
              },
              "cache": {
                "keepWarm": false
              },
              "sourceUrl": "https://developers.openai.com/api/docs/pricing",
              "retrievedAt": "2026-10-10"
            }
          ]
        }
        """;
}
