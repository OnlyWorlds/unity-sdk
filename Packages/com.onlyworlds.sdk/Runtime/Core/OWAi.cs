using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// One AI use, as <see cref="OWClient.AiUsesAsync"/> lists it (GET /account/ai; npm's <c>OwAiUse</c>): a named call a
    /// tool may make at POST /account/ai/{slug}. A tool calls a use by its name and sends the use's own body; it never
    /// sends a prompt.
    /// </summary>
    [Serializable]
    public class OWAiUse
    {
        /// <summary>The use's name in its route: POST /account/ai/{slug}. <see cref="OWClient.AiAsync(string, JObject, string, System.Threading.CancellationToken)"/> takes it.</summary>
        [JsonProperty("slug")]
        public string Slug;

        /// <summary>The tool that answers for its prompt and caps.</summary>
        [JsonProperty("owner")]
        public string Owner;

        /// <summary>
        /// Who runs the model (<see cref="OWAiProvider"/>): <c>anthropic</c> (the platform's key, or the account's own saved
        /// key once its rating is spent) or <c>jev</c> (a decisions gateway; an own key never runs it). As served: a word
        /// a later server adds reads as itself.
        /// </summary>
        [JsonProperty("provider")]
        public string Provider;

        /// <summary>The model a call on the platform's key runs.</summary>
        [JsonProperty("model")]
        public string Model;

        [JsonProperty("effort")]
        public string Effort;

        /// <summary>The sha256 of the use's system prompt, hex: two calls with the same hash ran the same prompt.</summary>
        [JsonProperty("prompt_sha256")]
        public string PromptSha256;

        /// <summary>The body names a world this account owns or is a member of.</summary>
        [JsonProperty("needs_world")]
        public bool NeedsWorld;

        /// <summary>What the body may hold, by name.</summary>
        [JsonProperty("input_caps")]
        public Dictionary<string, long> InputCaps;

        /// <summary>The most tokens one answer may take.</summary>
        [JsonProperty("output_cap")]
        public long OutputCap;

        /// <summary>
        /// The rating tokens a call needs left today to run (or a saved own key, for a use it can run): the output cap, or
        /// the use's worst case when it is larger.
        /// </summary>
        [JsonProperty("rating_need")]
        public long RatingNeed;

        /// <summary>The answer is checked JSON, not text.</summary>
        [JsonProperty("structured")]
        public bool Structured;

        /// <summary>A fixed price per call in rating tokens, or null: the call's actual cost is spent.</summary>
        [JsonProperty("price_tokens")]
        public long? PriceTokens;

        /// <summary>
        /// Calls per account per UTC day, or null. Past it, 429 <c>use_ceiling</c> (<see cref="OWApiError.IsUseCeiling"/>)
        /// until midnight UTC; a use with a ceiling runs one call at a time, and a second call while one runs is 429
        /// <c>use_in_flight</c> (<see cref="OWApiError.IsUseInFlight"/>).
        /// </summary>
        [JsonProperty("ceiling")]
        public long? Ceiling;

        /// <summary>This account's calls left today, or null when the use has no ceiling.</summary>
        [JsonProperty("calls_left")]
        public long? CallsLeft;

        /// <summary>
        /// False while the server's AI is off, the use is switched off, or it has spent its own budget for today (its calls
        /// answer 503 <c>ask_unavailable</c> with param <c>use</c>).
        /// </summary>
        [JsonProperty("on")]
        public bool On;

        /// <summary>
        /// True when a call by this account would be paid now, by the same check the call runs: <see cref="On"/>, the
        /// server's daily cap not reached, and today's token rating holding <see cref="RatingNeed"/> (or, with the rating
        /// spent, a saved AI key that runs this use). Offer the use only when this is true. False when a server that does
        /// not send it answers.
        /// </summary>
        [JsonProperty("can_pay")]
        public bool CanPay;
    }

    /// <summary>The words of <see cref="OWAiUse.Provider"/> (npm's <c>OwAiUse.provider</c>).</summary>
    public static class OWAiProvider
    {
        /// <summary>The platform's Anthropic key, or the account's own saved key once its rating is spent.</summary>
        public const string Anthropic = "anthropic";

        /// <summary>A decisions gateway; an own key never runs it.</summary>
        public const string Jev = "jev";
    }

    /// <summary>
    /// The <c>ask</c> use's body (POST /account/ai/ask; npm's <c>OwAiAskBody</c>), in its own wire spelling: the same
    /// fields as <see cref="OWClient.AskAsync"/> takes.
    /// </summary>
    [Serializable]
    public class OWAiAskBody
    {
        [JsonProperty("world_id")]
        public string WorldId;

        /// <summary>1 to 500 characters.</summary>
        [JsonProperty("question")]
        public string Question;

        /// <summary>1 to 8 elements as text, each up to 2,000 characters, 12,000 in all.</summary>
        [JsonProperty("context")]
        public List<string> Context;
    }

    /// <summary>
    /// The <c>sort</c> use's body (POST /account/ai/sort; npm's <c>OwAiSortBody</c>), in its own wire spelling: the same
    /// fields as <see cref="OWClient.SortAsync"/> takes. A note goes as <see cref="OWSortNote"/> does there; the slice is
    /// optional (null: not sent).
    /// </summary>
    public class OWAiSortBody
    {
        public string WorldId;

        public List<OWSortNote> Notes;

        public List<OWSortRef> Slice;
    }

    /// <summary>
    /// POST /account/ai/ask's 200 (npm's <c>OwAiAskAnswer</c>): <see cref="OWClient.AskAsync"/>'s answer, with the use's
    /// meter.
    /// </summary>
    [Serializable]
    public class OWAiAskAnswer : OWAskAnswer
    {
        /// <summary>Calls to this use this account has left today; null when the use has no daily ceiling.</summary>
        [JsonProperty("use_remaining")]
        public long? UseRemaining;

        /// <summary>
        /// True when this answer is the stored first answer for the call's idempotency key, billed nothing this time. Read
        /// from the <c>Idempotent-Replay</c> header, never from the body.
        /// </summary>
        [JsonIgnore]
        public bool WasReplay;
    }

    /// <summary>
    /// POST /account/ai/sort's 200 (npm's <c>OwAiSortAnswer</c>): <see cref="OWClient.SortAsync"/>'s answer, with the use's
    /// meter.
    /// </summary>
    [Serializable]
    public class OWAiSortAnswer : OWSortAnswer
    {
        /// <summary>Calls to this use this account has left today; null when the use has no daily ceiling.</summary>
        [JsonProperty("use_remaining")]
        public long? UseRemaining;

        /// <summary>As on <see cref="OWAiAskAnswer.WasReplay"/>.</summary>
        [JsonIgnore]
        public bool WasReplay;
    }

    /// <summary>
    /// Any AI use's 200, as served (npm's <c>Record&lt;string, unknown&gt; &amp; OwAiMeter</c>): the use's own answer,
    /// verbatim, and the meter every use adds.
    /// </summary>
    public class OWAiAnswer
    {
        /// <summary>The whole answer as served, the meter's keys included (<c>paid_by</c>, <c>model</c>, <c>usage</c>, ...).</summary>
        public JObject Answer;

        /// <summary>
        /// The answer's <c>use_remaining</c>: calls to this use this account has left today; null when the use has no daily
        /// ceiling (the token rating is then the only limit).
        /// </summary>
        public long? UseRemaining;

        /// <summary>As on <see cref="OWAiAskAnswer.WasReplay"/>.</summary>
        public bool WasReplay;
    }

    /// <summary>
    /// An AI door token (npm's <c>OwAiToken</c>), answered by the sign-in's code exchange beside the account token when
    /// <see cref="OWClient.CompleteSignInAsync"/> asked for one: an <c>ow_a_</c> token that opens only
    /// <see cref="OWClient.AiUsesAsync"/> and the AI uses it names, as the person and on their token rating.
    /// </summary>
    /// <remarks>
    /// Another use answers 403 <c>use_not_granted</c> (<see cref="OWApiError.IsUseNotGranted"/>), every other route 401
    /// <c>wrong_audience</c> (<see cref="OWApiError.IsWrongAudience"/>). It ends at <see cref="ExpiresAt"/> (midnight UTC at
    /// the latest), then answers 401 <c>key_revoked</c>. A client built on it may revoke it with
    /// <see cref="OWClient.RevokeAccountTokenAsync"/> and <see cref="TokenId"/>; revoking it touches no other token, and
    /// revoking the account token beside it leaves it live.
    /// </remarks>
    [Serializable]
    public class OWAiToken
    {
        /// <summary>The <c>ow_a_</c> AI door token, shown only here: keep it, the SDK stores none.</summary>
        [JsonProperty("token")]
        public string Token;

        [JsonProperty("token_id")]
        public string TokenId;

        /// <summary><c>bearer</c>.</summary>
        [JsonProperty("token_type")]
        public string TokenType;

        /// <summary><c>ai</c>.</summary>
        [JsonProperty("audience")]
        public string Audience;

        /// <summary>The uses it opens, as the server took them (each once, in the order sent).</summary>
        [JsonProperty("ai_uses")]
        public List<string> AiUses;

        /// <summary>When it stops working (ISO 8601, as served).</summary>
        [JsonProperty("expires_at")]
        public string ExpiresAt;
    }
}
