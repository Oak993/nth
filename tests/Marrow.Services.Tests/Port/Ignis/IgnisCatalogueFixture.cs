namespace Marrow.Services.Tests.Port.Ignis;

// Ignis price catalogue (ED-NEW-ignis-data): a trimmed copy of LiteLLM's model_prices_and_context_window.json (a few real
// entries as published on 2026-10-10, with the fields Ignis never reads mostly cut), plus entries named "*-test-*" that are
// not LiteLLM data and exist only to break a guardrail; and a schema v2 points file for the catalogue tests (its prices are
// test values, not provider prices).
internal static class IgnisCatalogueFixture
{
    public const string LiteLlm = """
        {
          "sample_spec": {"litellm_provider":"one of https://docs.litellm.ai/docs/providers","input_cost_per_token":0.0,"mode":"one of: chat, embedding"},
          "claude-opus-5-5": {"cache_creation_input_token_cost":5e-06,"cache_creation_input_token_cost_above_1hr":8e-06,"cache_creation_input_token_cost_batches":2.5e-06,"cache_read_input_token_cost":2e-07,"cache_read_input_token_cost_batches":1e-07,"input_cost_per_token":4e-06,"input_cost_per_token_batches":2e-06,"litellm_provider":"anthropic","max_input_tokens":1000000,"max_output_tokens":128000,"max_tokens":128000,"mode":"chat","output_cost_per_token":2e-05,"output_cost_per_token_batches":1e-05,"thinking_always_on":true,"prompt_cache_min_tokens":512},
          "claude-haiku-5-5": {"input_cost_per_token_above_100k_tokens_batches":2.5e-07,"output_cost_per_token_above_100k_tokens_batches":1.25e-06,"cache_creation_input_token_cost_above_100k_tokens_batches":3.125e-07,"cache_read_input_token_cost_above_100k_tokens_batches":2.5e-08,"cache_creation_input_token_cost":1.25e-07,"cache_creation_input_token_cost_above_1hr":2e-07,"cache_creation_input_token_cost_batches":6.25e-08,"cache_read_input_token_cost":1e-08,"cache_read_input_token_cost_batches":5e-09,"input_cost_per_token":1e-07,"input_cost_per_token_batches":5e-08,"litellm_provider":"anthropic","max_input_tokens":1000000,"max_output_tokens":128000,"max_tokens":128000,"mode":"chat","output_cost_per_token":5e-07,"output_cost_per_token_batches":2.5e-07,"thinking_always_on":false,"prompt_cache_min_tokens":512,"input_cost_per_token_above_100k_tokens":5e-07,"output_cost_per_token_above_100k_tokens":2.5e-06,"cache_creation_input_token_cost_above_100k_tokens":6.25e-07,"cache_creation_input_token_cost_above_1hr_above_100k_tokens":1e-06,"cache_read_input_token_cost_above_100k_tokens":5e-08},
          "claude-mythos-5": {"cache_creation_input_token_cost":1.25e-05,"cache_creation_input_token_cost_above_1hr":2e-05,"cache_creation_input_token_cost_batches":6.25e-06,"cache_read_input_token_cost":1e-06,"cache_read_input_token_cost_batches":5e-07,"input_cost_per_token":1e-05,"input_cost_per_token_batches":5e-06,"litellm_provider":"anthropic","max_input_tokens":1000000,"max_output_tokens":128000,"max_tokens":128000,"mode":"chat","output_cost_per_token":5e-05,"output_cost_per_token_batches":2.5e-05,"prompt_cache_min_tokens":512,"thinking_always_on":true},
          "gpt-6-sol": {"cache_creation_input_token_cost":2.5e-06,"cache_creation_input_token_cost_above_272k_tokens":5e-06,"cache_read_input_token_cost":2e-07,"cache_read_input_token_cost_above_272k_tokens":4e-07,"cache_read_input_token_cost_batches":1e-07,"cache_read_input_token_cost_above_272k_tokens_batches":2e-07,"cache_creation_input_token_cost_batches":1.25e-06,"cache_creation_input_token_cost_above_272k_tokens_batches":2.5e-06,"input_cost_per_token":2e-06,"input_cost_per_token_above_272k_tokens":4e-06,"input_cost_per_token_batches":1e-06,"input_cost_per_token_above_272k_tokens_batches":2e-06,"litellm_provider":"openai","max_input_tokens":922000,"max_output_tokens":128000,"max_tokens":128000,"mode":"chat","output_cost_per_token":1e-05,"output_cost_per_token_above_272k_tokens":1.5e-05,"output_cost_per_token_batches":5e-06,"output_cost_per_token_above_272k_tokens_batches":7.5e-06,"regional_processing_uplift_multiplier_eu":1.1,"regional_processing_uplift_multiplier_us":1.1,"input_cost_per_token_priority":4e-06},
          "gpt-5.5": {"cache_read_input_token_cost":5e-07,"cache_read_input_token_cost_above_272k_tokens":1e-06,"cache_read_input_token_cost_batches":2.5e-07,"cache_read_input_token_cost_above_272k_tokens_batches":5e-07,"input_cost_per_token":5e-06,"input_cost_per_token_above_272k_tokens":1e-05,"input_cost_per_token_batches":2.5e-06,"input_cost_per_token_above_272k_tokens_batches":5e-06,"litellm_provider":"openai","max_input_tokens":1050000,"max_output_tokens":128000,"max_tokens":128000,"mode":"chat","output_cost_per_token":3e-05,"output_cost_per_token_above_272k_tokens":4.5e-05,"output_cost_per_token_batches":1.5e-05,"output_cost_per_token_above_272k_tokens_batches":2.25e-05,"regional_processing_uplift_multiplier_eu":1.1,"regional_processing_uplift_multiplier_us":1.1},
          "deepseek-v4-pro": {"cache_creation_input_token_cost":0.0,"cache_read_input_token_cost":4.4e-08,"input_cost_per_token":1.32e-06,"input_cost_per_token_cache_hit":4.4e-08,"litellm_provider":"deepseek","max_input_tokens":1000000,"max_output_tokens":393216,"max_tokens":393216,"mode":"chat","output_cost_per_token":3.96e-06},
          "deepseek/deepseek-v4-pro": {"cache_creation_input_token_cost":0.0,"cache_read_input_token_cost":4.4e-08,"input_cost_per_token":1.32e-06,"input_cost_per_token_cache_hit":4.4e-08,"litellm_provider":"deepseek","max_input_tokens":1000000,"max_output_tokens":393216,"max_tokens":393216,"mode":"chat","output_cost_per_token":3.96e-06},
          "zai/glm-5.3": {"cache_creation_input_token_cost":0,"cache_read_input_token_cost":2.6e-07,"input_cost_per_token":1.4e-06,"litellm_provider":"zai","max_input_tokens":1000000,"max_output_tokens":128000,"mode":"chat","output_cost_per_token":4.4e-06},
          "meta/muse-spark-1.3": {"cache_read_input_token_cost":1.5e-07,"input_cost_per_token":1.25e-06,"litellm_provider":"meta","max_input_tokens":1048576,"max_output_tokens":131072,"max_tokens":131072,"mode":"chat","output_cost_per_token":4.25e-06},
          "chatgpt/gpt-6-sol": {"litellm_provider":"chatgpt","mode":"responses","max_input_tokens":922000,"max_output_tokens":128000,"max_tokens":128000},
          "gpt-image-1.5": {"cache_read_input_image_token_cost":2e-06,"cache_read_input_token_cost":1.25e-06,"cache_read_input_token_cost_batches":6.3e-07,"deprecation_date":"2026-12-01","input_cost_per_token":5e-06,"litellm_provider":"openai","mode":"image_generation","output_cost_per_token":1e-05,"input_cost_per_image_token":8e-06,"input_cost_per_image_token_batches":4e-06,"input_cost_per_token_batches":2.5e-06,"output_cost_per_image_token":3.2e-05,"output_cost_per_token_batches":5e-06},
          "1024-x-1024/50-steps/bedrock/amazon.nova-canvas-v1:0": {"deprecation_date":"2026-09-30","litellm_provider":"bedrock","max_input_tokens":2600,"mode":"image_generation","output_cost_per_image":0.06},
          "claude-test-negative": {"litellm_provider":"anthropic","mode":"chat","max_input_tokens":200000,"input_cost_per_token":-1e-06,"output_cost_per_token":5e-06},
          "claude-test-dear-cache": {"litellm_provider":"anthropic","mode":"chat","max_input_tokens":200000,"input_cost_per_token":1e-06,"output_cost_per_token":5e-06,"cache_read_input_token_cost":2e-06},
          "gpt-test-cheap-write": {"litellm_provider":"openai","mode":"responses","max_input_tokens":400000,"input_cost_per_token":1e-06,"output_cost_per_token":4e-06,"cache_read_input_token_cost":1e-07,"cache_creation_input_token_cost":5e-08},
          "gpt-test-no-window": {"litellm_provider":"openai","mode":"chat","input_cost_per_token":1e-06,"output_cost_per_token":4e-06},
          "gpt-test-no-cache": {"litellm_provider":"openai","mode":"chat","max_input_tokens":128000,"input_cost_per_token":3.0000000000000004e-07,"output_cost_per_token":1.2e-06}
        }
        """;

    public const string Points = """
        {"schemaVersion":2,"version":"2026.09.20",
         "providers":[
          {"provider":"claude","billing":"perToken","ttlMainSeconds":3600,"ttlSubagentSeconds":300,"keepWarm":true,"catalogue":["anthropic"],"sourceUrl":"https://platform.claude.com/docs/en/build-with-claude/prompt-caching","retrievedAt":"2026-09-20"},
          {"provider":"codex","billing":"perToken","ttlMainSeconds":1800,"keepWarm":false,"catalogue":["openai"],"sourceUrl":"https://developers.openai.com/api/docs/guides/prompt-caching","retrievedAt":"2026-09-20"},
          {"provider":"deepseek","billing":"perToken","keepWarm":false,"catalogue":["deepseek"],"sourceUrl":"https://api-docs.deepseek.com/quick_start/pricing","retrievedAt":"2026-09-20"},
          {"provider":"zai","billing":"perToken","keepWarm":false,"catalogue":["zai"],"sourceUrl":"https://docs.z.ai/guides/overview/pricing","retrievedAt":"2026-09-20"},
          {"provider":"meta","billing":"perToken","keepWarm":false,"catalogue":["meta","meta_llama"],"sourceUrl":"https://example.org/meta-pricing","retrievedAt":"2026-09-20"},
          {"provider":"copilot","billing":"flat","keepWarm":false,"notes":"A plan, not billed per token.","sourceUrl":"https://example.org/copilot-plans","retrievedAt":"2026-09-20"}],
         "models":[
          {"provider":"claude","ids":["claude-opus-5-5"],"aliases":["opus"],"displayName":"Opus 5.5","contextWindow":1000000,
           "prices":{"input":4,"output":20,"cacheRead":0.2,"cacheWrite5m":5,"cacheWrite1h":8},
           "cache":{"ttlMainSeconds":3600,"ttlSubagentSeconds":300,"keepWarm":true},"helperModel":"claude-haiku-5-5",
           "sourceUrl":"https://platform.claude.com/docs/en/about-claude/pricing","retrievedAt":"2026-09-20"},
          {"provider":"claude","ids":["claude-haiku-5-5"],"aliases":["haiku"],"displayName":"Haiku 5.5","contextWindow":1000000,
           "prices":{"input":0.2,"output":1,"cacheRead":0.02,"cacheWrite5m":0.25,"cacheWrite1h":0.4},
           "cache":{"ttlMainSeconds":3600,"ttlSubagentSeconds":300,"keepWarm":true},"tidyAt":900000,"tidyReason":"Set by the points",
           "sourceUrl":"https://platform.claude.com/docs/en/about-claude/pricing","retrievedAt":"2026-08-01"},
          {"provider":"codex","ids":["gpt-6-sol"],"aliases":[],"displayName":"GPT-6 Sol","contextWindow":1050000,
           "prices":{"input":20,"output":100,"cacheRead":2,"cacheWrites":[{"ttlSeconds":1800,"price":25}]},
           "cache":{"ttlMainSeconds":1800,"keepWarm":false},
           "sourceUrl":"https://developers.openai.com/api/docs/pricing","retrievedAt":"2026-08-01"}]}
        """;
}
