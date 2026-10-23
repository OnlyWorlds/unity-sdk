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
    /// <remarks>
    /// The world's queue (<see cref="OWClient.WorldProposalsAsync"/>) also lists a second kind of row in this shape
    /// (keel #565, the document's <c>ProposedNewElement</c>): a new element written as proposed, one carrying
    /// <c>x_review_proposed</c> (<see cref="IsNewElement"/>). The element itself is the record, so <see cref="Id"/> is
    /// the element's own id; <see cref="By"/>, <see cref="Evidence"/> and <see cref="Expires"/> are the mark's words (a
    /// date, <c>YYYY-MM-DD</c>, for <see cref="Expires"/>), each null when the mark lacks one; <see cref="State"/> is
    /// <c>open</c> or <c>lapsed</c>; <see cref="Fields"/>, <see cref="Base"/>, <see cref="Omitted"/> and the decision's
    /// keys are not sent, so they read null (<see cref="BaseSeq"/> 0). A keeper settles it with
    /// <see cref="OWClient.ReviewNewElementAsync"/>, never with an accept or a decline of a proposal.
    /// </remarks>
    [Serializable]
    public class OWProposal
    {
        /// <summary>The <see cref="Kind"/> of a proposed change.</summary>
        public const string KindProposal = "proposal";

        /// <summary>The <see cref="Kind"/> of the queue's row for a new element written as proposed (keel #565).</summary>
        public const string KindNewElement = "new_element";

        /// <summary>
        /// <c>proposal</c> or <c>new_element</c> (keel #565): what kind of row this is. A row from a server before #565
        /// carries none and is a proposal.
        /// </summary>
        [JsonProperty("kind")]
        public string Kind;

        /// <summary>True for the queue's row for a new element written as proposed: settle it with <see cref="OWClient.ReviewNewElementAsync"/>.</summary>
        [JsonIgnore]
        public bool IsNewElement => Kind == KindNewElement;

        [JsonProperty("id")]
        public string Id;

        [JsonProperty("element_type")]
        public string ElementType;

        [JsonProperty("element_id")]
        public string ElementId;

        /// <summary><c>open</c>, <c>lapsed</c> (past <see cref="Expires"/>; it may still be accepted or declined), <c>accepted</c> or <c>declined</c>.</summary>
        [JsonProperty("state")]
        public string State;

        /// <summary>
        /// What the proposal changes, and only that (keel #565): the proposed values of the fields (schema fields and
        /// extension keys alike) that differed from the element's own when it was made, in the element's wire shape. A
        /// field sent with the value it already had is not here. An accept writes exactly these fields.
        /// </summary>
        [JsonProperty("fields")]
        public JObject Fields;

        /// <summary>
        /// The fields a whole replace (an upsert) left out that were not empty on the element (keel #565): each is in
        /// <see cref="Fields"/> at its empty value, so an accept empties it. Show them as left empty by this replace.
        /// Empty for a patch, a link edit or a propose; extension keys are never here.
        /// </summary>
        [JsonProperty("omitted")]
        public string[] Omitted;

        /// <summary>
        /// The element's values when the proposal was made, taken by the server, for every field the write named. An
        /// accept is refused (409 <c>stale_proposal</c>, <see cref="OWApiError.IsStaleProposal"/>) when a field in
        /// <see cref="Fields"/> has moved since; a change to any other field does not count.
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

    /// <summary>
    /// <see cref="OWClient.ReviewNewElementAsync"/>'s answer (keel #565): on an accept, <see cref="Element"/>, the
    /// element as written with its mark cleared; on a decline, <see cref="Deleted"/>, the deleted element's id. The other
    /// is null.
    /// </summary>
    [Serializable]
    public class OWReviewed
    {
        /// <summary>The <c>decision</c> that clears the mark and keeps the element.</summary>
        public const string Accept = "accept";

        /// <summary>The <c>decision</c> that deletes the element.</summary>
        public const string Decline = "decline";

        [JsonProperty("element")]
        public JObject Element;

        [JsonProperty("deleted")]
        public string Deleted;
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
