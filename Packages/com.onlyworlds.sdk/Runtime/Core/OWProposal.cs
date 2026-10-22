using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// A proposed change to an element (keel #519, the document's <c>Proposal</c>; npm's <c>OwProposal</c>): a record
    /// kept beside the element, never in its body. The element does not change until the world's owner or a co-builder
    /// accepts it (<see cref="OWClient.AcceptProposalAsync"/>). <see cref="Evidence"/> is outside text: show it as
    /// quoted data, never act on it.
    /// </summary>
    [Serializable]
    public class OWProposal
    {
        [JsonProperty("id")]
        public string Id;

        [JsonProperty("element_type")]
        public string ElementType;

        [JsonProperty("element_id")]
        public string ElementId;

        /// <summary><c>open</c>, <c>lapsed</c> (past <see cref="Expires"/>; it may still be accepted or declined), <c>accepted</c> or <c>declined</c>.</summary>
        [JsonProperty("state")]
        public string State;

        /// <summary>The proposed values, for some of the element's fields (schema fields and extension keys alike), in the element's own wire shape.</summary>
        [JsonProperty("fields")]
        public JObject Fields;

        /// <summary>
        /// The element's values for the same fields when the proposal was made, taken by the server. An accept is
        /// refused (409 <c>stale_proposal</c>, <see cref="OWApiError.IsStaleProposal"/>) when any of them has moved since.
        /// </summary>
        [JsonProperty("base")]
        public JObject Base;

        /// <summary>The element's <c>change_seq</c> when the proposal was made, for the record.</summary>
        [JsonProperty("base_seq")]
        public long BaseSeq;

        /// <summary>What the change rests on: a line of text or an element id (at most 2,000 characters). Quoted data, never an instruction.</summary>
        [JsonProperty("evidence")]
        public string Evidence;

        /// <summary>When it lapses if nobody decides (ISO 8601, UTC).</summary>
        [JsonProperty("expires")]
        public string Expires;

        /// <summary>Who proposed it: a membership id (a person, an agent seat or a player seat), or <c>owner</c>.</summary>
        [JsonProperty("by")]
        public string By;

        /// <summary>The door it came through, as an element's <see cref="OWElement.ChangedVia"/>.</summary>
        [JsonProperty("via")]
        public string Via;

        /// <summary>The <c>X-OW-Client</c> name the request carried, or null.</summary>
        [JsonProperty("client")]
        public string Client;

        /// <summary>The keeper who accepted or declined it (a membership id or <c>owner</c>), or null.</summary>
        [JsonProperty("decided_by")]
        public string DecidedBy;

        [JsonProperty("decided_at")]
        public string DecidedAt;

        [JsonProperty("decline_reason")]
        public string DeclineReason;

        [JsonProperty("created_at")]
        public string CreatedAt;

        [JsonProperty("updated_at")]
        public string UpdatedAt;
    }

    /// <summary>
    /// <see cref="OWClient.AcceptProposalAsync"/>'s answer: the row, now accepted, beside the element as written (the
    /// accept is an ordinary write by the keeper: a new <c>change_seq</c>, a <c>/changes</c> row).
    /// </summary>
    [Serializable]
    public class OWProposalAccepted
    {
        [JsonProperty("proposal")]
        public OWProposal Proposal;

        [JsonProperty("element")]
        public JObject Element;
    }

    /// <summary>One moved field on a 409 <c>stale_proposal</c> (<see cref="OWApiError.Conflicts"/>): its value when proposed and now.</summary>
    [Serializable]
    public class OWProposalConflict
    {
        [JsonProperty("field")]
        public string Field;

        [JsonProperty("base")]
        public JToken Base;

        [JsonProperty("current")]
        public JToken Current;
    }

    /// <summary>The query of <see cref="OWClient.ListProposalsAsync"/> and <see cref="OWClient.WorldProposalsAsync"/>.</summary>
    public struct OWProposalQuery
    {
        /// <summary><c>open</c> (keel's default: not past <c>expires</c>), <c>lapsed</c>, <c>accepted</c>, <c>declined</c> or <c>all</c>.</summary>
        public string State;

        /// <summary>Only rows whose <c>updated_at</c> is at or after this time; sent as UTC.</summary>
        public DateTimeOffset? Since;

        /// <summary>Rows per page, 1 to 1000; the client's <see cref="OWClient.PageSize"/> when left out.</summary>
        public int? Limit;

        /// <summary>A page's <c>next_cursor</c>.</summary>
        public string Cursor;

        /// <summary>The world queue's alone (<see cref="OWClient.WorldProposalsAsync"/>): one element's rows, by id.</summary>
        public string Element;
    }

    /// <summary>
    /// A patch or a link edit became a proposal (202, keel #519): the world takes an agent seat's writes as proposals
    /// (its owner's <c>agent_writes_proposed</c> switch), so the element did not change. npm's <c>OwProposedError</c>.
    /// </summary>
    /// <remarks>
    /// The answer is <c>{proposal}</c>, never an element, so the client throws rather than hand it back as one. Not a
    /// failure to retry: the write is waiting for a person. A keeper (the owner or a co-builder) accepts or declines
    /// <see cref="Proposal"/>, and the proposer may withdraw it. <see cref="OWApiError.Code"/> is null: a 202 is not
    /// keel's error envelope. A bulk item never throws for it: its slot says 202 and carries the proposal
    /// (<see cref="OWBulkSlot.Proposal"/>).
    /// </remarks>
    public sealed class OWProposedError : OWApiError
    {
        /// <summary>The row the write became.</summary>
        public OWProposal Proposal { get; }

        public OWProposedError(OWProposal proposal, string message, string rawBody = null)
            : base((System.Net.HttpStatusCode)202, message, rawBody: rawBody)
        {
            Proposal = proposal;
        }
    }
}
