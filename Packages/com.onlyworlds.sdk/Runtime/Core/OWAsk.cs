using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// <see cref="OWClient.AskAsync"/>'s 200 (POST /account/ask; npm's <c>OwAskAnswer</c>): keel's AI proxy's answer to
    /// one question, from the context sent and nothing else, and what the account has left today.
    /// </summary>
    [Serializable]
    public class OWAskAnswer
    {
        /// <summary>The answer, from the context only; <c>The world doesn't say.</c> when the context does not hold one.</summary>
        [JsonProperty("answer")]
        public string Answer;

        /// <summary>The model that answered.</summary>
        [JsonProperty("model")]
        public string Model;

        /// <summary>Why the model stopped, as it said; null when it said nothing.</summary>
        [JsonProperty("stop_reason")]
        public string StopReason;

        [JsonProperty("usage")]
        public OWAiUsage Usage;

        /// <summary>
        /// How many more calls of this one's size fit in what the account has left today: a count of answers, never
        /// tokens, and it never over-promises.
        /// </summary>
        [JsonProperty("remaining")]
        public int Remaining;

        /// <summary>The account's tokens left today after this call.</summary>
        [JsonProperty("tokens_remaining")]
        public long TokensRemaining;

        /// <summary>The account's token rating: its daily budget in tokens, one meter with sort and the Council.</summary>
        [JsonProperty("token_rating")]
        public long TokenRating;
    }

    /// <summary>The tokens one call of the AI proxy spent, as the model counted them.</summary>
    [Serializable]
    public class OWAiUsage
    {
        [JsonProperty("input_tokens")]
        public long InputTokens;

        [JsonProperty("output_tokens")]
        public long OutputTokens;
    }

    /// <summary>
    /// One element the notes may be about (npm's <c>OwSortRef</c>): a row of <see cref="OWClient.SortAsync"/>'s slice
    /// going in, and a row's target or candidate coming back, where keel copies the name, type and anchor from the slice,
    /// never from the model. <see cref="Type"/> is lower case going in; keel answers with its own spelling.
    /// </summary>
    [Serializable]
    public class OWSortRef
    {
        [JsonProperty("id")]
        public string Id;

        [JsonProperty("type")]
        public string Type;

        [JsonProperty("name")]
        public string Name;

        /// <summary>A short phrase that tells two same-named elements apart (up to 120 characters), or null: not sent.</summary>
        [JsonProperty("anchor")]
        public string Anchor;
    }

    /// <summary>
    /// A note for <see cref="OWClient.SortAsync"/> (npm's <c>OwSortNote</c>): its words, a picture already uploaded into
    /// this world (<c>https://media.onlyworlds.com/u/&lt;world id&gt;/&lt;name&gt;</c>, no query), read as a photographed
    /// note, or both. A string is a note of words alone: <c>(OWSortNote)"Mira runs the ferry."</c>.
    /// </summary>
    /// <remarks>
    /// Sent as keel takes it: words alone as a plain string, a note with a picture as <c>{text?, image_url}</c>. A note
    /// with neither is refused before sending.
    /// </remarks>
    public class OWSortNote
    {
        /// <summary>The note's words, or null.</summary>
        public string Text;

        /// <summary>The picture's URL, or null.</summary>
        public string ImageUrl;

        public static implicit operator OWSortNote(string text) => new OWSortNote { Text = text };

        /// <summary>The note as the body carries it; null when it holds neither words nor a picture.</summary>
        internal JToken ToJson()
        {
            if (ImageUrl == null) return Text == null ? null : new JValue(Text);

            var note = new JObject();
            if (Text != null) note["text"] = Text;
            note["image_url"] = ImageUrl;
            return note;
        }
    }

    /// <summary>
    /// One proposed change of <see cref="OWClient.SortAsync"/>'s answer, for the user to review (npm's <c>OwSortRow</c>,
    /// its four kinds in one class): <see cref="Kind"/> says which members it carries. Every id in a row is from the slice.
    /// </summary>
    /// <remarks>
    /// <c>make</c>: a new element the notes describe, <see cref="Proposal"/>. <c>add_to</c>: <see cref="Value"/> to append
    /// (<see cref="Op"/> <c>append</c>) to <see cref="Field"/> of <see cref="Target"/>. <c>link</c>: a link from
    /// <see cref="Target"/> through <see cref="Field"/> to <see cref="ValueId"/> (<see cref="ValueName"/>). <c>ask</c>: a
    /// <see cref="Question"/> for the user, whose pick among <see cref="Candidates"/> fills <see cref="Then"/>'s target.
    /// </remarks>
    [Serializable]
    public class OWSortRow
    {
        /// <summary><c>make</c>, <c>add_to</c>, <c>link</c> or <c>ask</c>.</summary>
        [JsonProperty("kind")]
        public string Kind;

        /// <summary>The note's line the row came from.</summary>
        [JsonProperty("source_line")]
        public string SourceLine;

        /// <summary><c>make</c>: the new element's type, name and description.</summary>
        [JsonProperty("proposal")]
        public OWSortProposal Proposal;

        /// <summary><c>add_to</c> and <c>link</c>: the slice element the row changes.</summary>
        [JsonProperty("target")]
        public OWSortRef Target;

        /// <summary><c>add_to</c> and <c>link</c>: the field written.</summary>
        [JsonProperty("field")]
        public string Field;

        /// <summary><c>add_to</c>: <c>append</c>.</summary>
        [JsonProperty("op")]
        public string Op;

        /// <summary><c>add_to</c>: the text to append.</summary>
        [JsonProperty("value")]
        public string Value;

        /// <summary><c>link</c>: the slice element linked to.</summary>
        [JsonProperty("value_id")]
        public string ValueId;

        [JsonProperty("value_name")]
        public string ValueName;

        /// <summary><c>ask</c>: the question.</summary>
        [JsonProperty("question")]
        public string Question;

        /// <summary><c>ask</c>: the slice elements the name could be.</summary>
        [JsonProperty("candidates")]
        public List<OWSortRef> Candidates;

        /// <summary><c>ask</c>, when sent: the <c>add_to</c> or <c>link</c> the question stands for, its target the user's pick.</summary>
        [JsonProperty("then")]
        public OWSortThen Then;
    }

    /// <summary>The new element a <c>make</c> row proposes.</summary>
    [Serializable]
    public class OWSortProposal
    {
        [JsonProperty("type")]
        public string Type;

        [JsonProperty("name")]
        public string Name;

        [JsonProperty("description")]
        public string Description;
    }

    /// <summary>
    /// The change an <c>ask</c> row stands for (npm's <c>then</c>): an <c>add_to</c> or <c>link</c> row without its line
    /// and target.
    /// </summary>
    [Serializable]
    public class OWSortThen
    {
        [JsonProperty("kind")]
        public string Kind;

        [JsonProperty("field")]
        public string Field;

        [JsonProperty("op")]
        public string Op;

        [JsonProperty("value")]
        public string Value;

        [JsonProperty("value_id")]
        public string ValueId;

        [JsonProperty("value_name")]
        public string ValueName;
    }

    /// <summary>A line the sorter could not place, or a row keel refused (an id not from the slice), and why.</summary>
    [Serializable]
    public class OWSortNotProposed
    {
        [JsonProperty("source_line")]
        public string SourceLine;

        [JsonProperty("reason")]
        public string Reason;
    }

    /// <summary>
    /// <see cref="OWClient.SortAsync"/>'s 200 (POST /account/sort; npm's <c>OwSortAnswer</c>): proposed changes for the
    /// user to review. Nothing is written to the world.
    /// </summary>
    [Serializable]
    public class OWSortAnswer
    {
        [JsonProperty("rows")]
        public List<OWSortRow> Rows;

        [JsonProperty("not_proposed")]
        public List<OWSortNotProposed> NotProposed;

        [JsonProperty("model")]
        public string Model;

        [JsonProperty("stop_reason")]
        public string StopReason;

        [JsonProperty("usage")]
        public OWAiUsage Usage;

        /// <summary>How many more calls of this one's size fit in what the account has left today; ask and sort spend from one rating.</summary>
        [JsonProperty("remaining")]
        public int Remaining;

        [JsonProperty("tokens_remaining")]
        public long TokensRemaining;

        [JsonProperty("token_rating")]
        public long TokenRating;
    }

    /// <summary>
    /// <see cref="OWClient.AllowanceAsync"/>'s 200 (GET /account/allowance; npm's <c>OwAllowance</c>): the account's token
    /// rating, the one daily meter ask, sort and the Council spend from, and what is left of it today.
    /// </summary>
    [Serializable]
    public class OWAllowance
    {
        /// <summary>The daily budget in tokens.</summary>
        [JsonProperty("token_rating")]
        public long TokenRating;

        /// <summary>Tokens spent today (UTC); may pass the rating by the size of the last call.</summary>
        [JsonProperty("tokens_used_today")]
        public long TokensUsedToday;

        /// <summary>Tokens left today, never below 0.</summary>
        [JsonProperty("tokens_remaining")]
        public long TokensRemaining;

        /// <summary>When the day's tokens reset, the next midnight UTC: ISO 8601, as served.</summary>
        [JsonProperty("resets_at")]
        public string ResetsAt;
    }
}
