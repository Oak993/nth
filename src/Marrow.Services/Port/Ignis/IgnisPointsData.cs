// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-engine; PLAN unit U1; schema v2 and the 2026-10-10 refresh: ED-NEW-ignis-data). The
// model points compiled into nth (the .csproj is protected, so the file is a C# const string, like ModelPriceData.cs). The same
// JSON is the feed file (work/ignis-feed/ignis/model-points.json). Every price is US dollars per million tokens as printed on the
// official page named by the model's sourceUrl on its retrievedAt day (Anthropic: the Claude pricing page; OpenAI: each model's
// API page, whose long-context rule prices a prompt above 272K tokens at 2x input, cache read and cache write and 1.5x output);
// none is estimated. Claude cache lifetimes: main chat 1 h, sub-agents 5 min (Claude Code's subscription defaults; PLAN lead
// decision). OpenAI: GPT-5.6 and later keep a prefix 30 minutes and charge writes at 1.25x input (cacheWrites, ttlSeconds 1800);
// earlier models name no fixed lifetime and no write price (left out). Codex keep-warm is off (PLAN lead decision). Every nth
// provider has a rule (billing, lifetimes, keep-warm, the price catalogue providers that map to it) from its official page;
// a lifetime the research left UNCONFIRMED is left out. Haiku 4.5's context window is the one its overview page gave before
// (the 2026-10-10 research left it UNCONFIRMED); Opus 4.7 and 4.6 are left out (their API ids were UNCONFIRMED).
namespace Marrow.Services.Port.Ignis;

public static class IgnisPointsData
{
    public const string Json = """
        {
          "schemaVersion": 2,
          "version": "2026.10.10",
          "providers": [
            {
              "provider": "codex",
              "billing": "perToken",
              "ttlMainSeconds": 1800,
              "keepWarm": false,
              "catalogue": [
                "openai"
              ],
              "notes": "An API key pays per token; ChatGPT sign-in draws plan allowance and credits (cached input at the cached credit rate, no write charge). GPT-5.6 and later keep a prefix 30 minutes after its last use and charge writes at 1.25x input; earlier models hold it 5 to 10 minutes idle (or 24 hours on extended retention), so their entries name no lifetime. Keep-warm stays off (lead decision).",
              "sourceUrl": "https://developers.openai.com/api/docs/guides/prompt-caching",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "claude",
              "billing": "perToken",
              "ttlMainSeconds": 3600,
              "ttlSubagentSeconds": 300,
              "keepWarm": true,
              "catalogue": [
                "anthropic"
              ],
              "notes": "Subscription within plan usage: main conversation 1 hour, everything else 5 minutes. API key, usage credits or a cloud provider: 5 minutes for both. Reads refresh the lifetime at the read price.",
              "sourceUrl": "https://code.claude.com/docs/en/prompt-caching",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "cursor",
              "billing": "perToken",
              "keepWarm": false,
              "catalogue": [],
              "notes": "Plan pools charged at each model's API price (plus a Cursor token rate on third-party models); Cursor makes the request, so its cache cannot be pinged or read.",
              "sourceUrl": "https://cursor.com/docs/models-and-pricing",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "opencode",
              "billing": "perToken",
              "keepWarm": false,
              "catalogue": [],
              "notes": "Zen is pay-as-you-go per 1M tokens with its own price table; caching follows the routed model.",
              "sourceUrl": "https://opencode.ai/docs/zen/",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "opencodeGo",
              "billing": "flat",
              "keepWarm": false,
              "catalogue": [],
              "notes": "A monthly plan whose limits are dollar caps per model at Zen token prices.",
              "sourceUrl": "https://opencode.ai/docs/go/",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "grok",
              "billing": "perToken",
              "keepWarm": false,
              "catalogue": [
                "xai"
              ],
              "notes": "Automatic caching; entries can be evicted at any time and no lifetime is documented.",
              "sourceUrl": "https://docs.x.ai/developers/advanced-api-usage/prompt-caching",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "deepseek",
              "billing": "perToken",
              "keepWarm": false,
              "catalogue": [
                "deepseek"
              ],
              "notes": "Automatic disk cache cleared usually within a few hours to a few days; no write charge. Off-peak prices are half the peak prices the catalogue lists.",
              "sourceUrl": "https://api-docs.deepseek.com/guides/kv_cache",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "meta",
              "billing": "perToken",
              "keepWarm": false,
              "catalogue": [
                "meta",
                "meta_llama"
              ],
              "notes": "Automatic caching; prompt_cache_retention 24h on the Responses API instead of pings. No write charge listed.",
              "sourceUrl": "https://dev.meta.ai/docs/prompt-caching",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "zai",
              "billing": "perToken",
              "keepWarm": false,
              "catalogue": [
                "zai"
              ],
              "notes": "Implicit caching with no documented lifetime; the GLM Coding Plan counts prompts against a quota instead of tokens.",
              "sourceUrl": "https://docs.z.ai/guides/capabilities/cache",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "devin",
              "billing": "flat",
              "keepWarm": false,
              "catalogue": [],
              "notes": "A token budget per plan priced at API list prices; extra usage at API list prices; Enterprise bills compute units.",
              "sourceUrl": "https://docs.devin.ai/desktop/accounts/quota",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "antigravity",
              "billing": "flat",
              "keepWarm": false,
              "catalogue": [
                "gemini"
              ],
              "notes": "Quota refreshed every five hours up to a weekly limit; no token counts are shown, so catalogue prices are only API-equivalent.",
              "sourceUrl": "https://antigravity.google/docs/plans",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "copilot",
              "billing": "perToken",
              "keepWarm": false,
              "catalogue": [],
              "notes": "AI credits per token since 2026-06-01 (1 credit = $0.01); annual plans kept on request billing pay per premium request. Caching is the upstream vendor's.",
              "sourceUrl": "https://docs.github.com/en/copilot/reference/copilot-billing/models-and-pricing",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "commandcode",
              "billing": "perToken",
              "keepWarm": false,
              "catalogue": [],
              "notes": "Plans include credits drawn per model at the underlying API rate; extra credits at API rate with no markup.",
              "sourceUrl": "https://commandcode.ai/pricing",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "pi",
              "billing": "perToken",
              "keepWarm": false,
              "catalogue": [],
              "notes": "A harness over the user's own key or subscription; PI_CACHE_RETENTION=long asks Anthropic for 1 hour and OpenAI for 24 hours.",
              "sourceUrl": "https://app.unpkg.com/@earendil-works/pi-coding-agent@0.80.9/files/README.md",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "openrouter",
              "billing": "perToken",
              "keepWarm": false,
              "catalogue": [
                "openrouter"
              ],
              "notes": "Credits per token; caching passes through to the routed provider (Anthropic 5 minutes or 1 hour, others automatic).",
              "sourceUrl": "https://openrouter.ai/docs/guides/best-practices/prompt-caching",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "local",
              "billing": "none",
              "keepWarm": false,
              "catalogue": [],
              "notes": "Runs on the user's own hardware: no money is spent or saved.",
              "sourceUrl": "https://raw.githubusercontent.com/ggml-org/llama.cpp/master/tools/server/README.md",
              "retrievedAt": "2026-10-10"
            }
          ],
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
                "claude-fable-5"
              ],
              "aliases": [],
              "displayName": "Fable 5",
              "contextWindow": 1000000,
              "prices": {
                "input": 10,
                "output": 50,
                "cacheRead": 1,
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
              "sourceUrl": "https://platform.claude.com/docs/en/about-claude/pricing",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "claude",
              "ids": [
                "claude-opus-4-8"
              ],
              "aliases": [],
              "displayName": "Opus 4.8",
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
              "sourceUrl": "https://platform.claude.com/docs/en/about-claude/pricing",
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
              "sourceUrl": "https://platform.claude.com/docs/en/about-claude/pricing",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "claude",
              "ids": [
                "claude-sonnet-4-6"
              ],
              "aliases": [],
              "displayName": "Sonnet 4.6",
              "contextWindow": 200000,
              "prices": {
                "input": 3,
                "output": 15,
                "cacheRead": 0.3,
                "cacheWrite5m": 3.75,
                "cacheWrite1h": 6,
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
                "claude-sonnet-4-6[1m]"
              ],
              "aliases": [],
              "displayName": "Sonnet 4.6",
              "contextWindow": 1000000,
              "prices": {
                "input": 3,
                "output": 15,
                "cacheRead": 0.3,
                "cacheWrite5m": 3.75,
                "cacheWrite1h": 6,
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
                "cacheRead": 1,
                "cacheWrites": [
                  {
                    "ttlSeconds": 1800,
                    "price": 12.5
                  }
                ],
                "steps": [
                  {
                    "aboveTokens": 272000,
                    "input": 20,
                    "output": 75,
                    "cacheRead": 2,
                    "cacheWrites": [
                      {
                        "ttlSeconds": 1800,
                        "price": 25
                      }
                    ]
                  }
                ]
              },
              "cache": {
                "ttlMainSeconds": 1800,
                "keepWarm": false
              },
              "sourceUrl": "https://developers.openai.com/api/docs/models/gpt-6-astra",
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
                "cacheWrites": [
                  {
                    "ttlSeconds": 1800,
                    "price": 2.5
                  }
                ],
                "steps": [
                  {
                    "aboveTokens": 272000,
                    "input": 4,
                    "output": 15,
                    "cacheRead": 0.2,
                    "cacheWrites": [
                      {
                        "ttlSeconds": 1800,
                        "price": 5
                      }
                    ]
                  }
                ]
              },
              "cache": {
                "ttlMainSeconds": 1800,
                "keepWarm": false
              },
              "sourceUrl": "https://developers.openai.com/api/docs/models/gpt-6.1-sol",
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
                "cacheWrites": [
                  {
                    "ttlSeconds": 1800,
                    "price": 2.5
                  }
                ],
                "steps": [
                  {
                    "aboveTokens": 272000,
                    "input": 4,
                    "output": 15,
                    "cacheRead": 0.4,
                    "cacheWrites": [
                      {
                        "ttlSeconds": 1800,
                        "price": 5
                      }
                    ]
                  }
                ]
              },
              "cache": {
                "ttlMainSeconds": 1800,
                "keepWarm": false
              },
              "sourceUrl": "https://developers.openai.com/api/docs/models/gpt-6-sol",
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
                "cacheWrites": [
                  {
                    "ttlSeconds": 1800,
                    "price": 0.125
                  }
                ],
                "steps": [
                  {
                    "aboveTokens": 272000,
                    "input": 0.2,
                    "output": 0.75,
                    "cacheRead": 0.02,
                    "cacheWrites": [
                      {
                        "ttlSeconds": 1800,
                        "price": 0.25
                      }
                    ]
                  }
                ]
              },
              "cache": {
                "ttlMainSeconds": 1800,
                "keepWarm": false
              },
              "sourceUrl": "https://developers.openai.com/api/docs/models/gpt-6-luna",
              "retrievedAt": "2026-10-10"
            },
            {
              "provider": "codex",
              "ids": [
                "gpt-5.6-sol"
              ],
              "aliases": [
                "gpt-5.6"
              ],
              "displayName": "GPT-5.6 Sol",
              "contextWindow": 1050000,
              "prices": {
                "input": 4,
                "output": 20,
                "cacheRead": 0.4,
                "cacheWrites": [
                  {
                    "ttlSeconds": 1800,
                    "price": 5
                  }
                ],
                "steps": [
                  {
                    "aboveTokens": 272000,
                    "input": 8,
                    "output": 30,
                    "cacheRead": 0.8,
                    "cacheWrites": [
                      {
                        "ttlSeconds": 1800,
                        "price": 10
                      }
                    ]
                  }
                ]
              },
              "cache": {
                "ttlMainSeconds": 1800,
                "keepWarm": false
              },
              "sourceUrl": "https://developers.openai.com/api/docs/models/gpt-5.6-sol",
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
                "cacheWrites": [
                  {
                    "ttlSeconds": 1800,
                    "price": 2.5
                  }
                ],
                "steps": [
                  {
                    "aboveTokens": 272000,
                    "input": 4,
                    "output": 18,
                    "cacheRead": 0.4,
                    "cacheWrites": [
                      {
                        "ttlSeconds": 1800,
                        "price": 5
                      }
                    ]
                  }
                ]
              },
              "cache": {
                "ttlMainSeconds": 1800,
                "keepWarm": false
              },
              "sourceUrl": "https://developers.openai.com/api/docs/models/gpt-5.6-terra",
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
                "cacheWrites": [
                  {
                    "ttlSeconds": 1800,
                    "price": 0.25
                  }
                ],
                "steps": [
                  {
                    "aboveTokens": 272000,
                    "input": 0.4,
                    "output": 1.8,
                    "cacheRead": 0.04,
                    "cacheWrites": [
                      {
                        "ttlSeconds": 1800,
                        "price": 0.5
                      }
                    ]
                  }
                ]
              },
              "cache": {
                "ttlMainSeconds": 1800,
                "keepWarm": false
              },
              "sourceUrl": "https://developers.openai.com/api/docs/models/gpt-5.6-luna",
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
                    "cacheRead": 1
                  }
                ]
              },
              "cache": {
                "keepWarm": false
              },
              "sourceUrl": "https://developers.openai.com/api/docs/models/gpt-5.5",
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
              "sourceUrl": "https://developers.openai.com/api/docs/models/gpt-5.4",
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
              "sourceUrl": "https://developers.openai.com/api/docs/models/gpt-5.3-codex",
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
              "sourceUrl": "https://developers.openai.com/api/docs/models/gpt-5.4-mini",
              "retrievedAt": "2026-10-10"
            }
          ]
        }
        """;
}
