using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// A structured error returned by the OnlyWorlds v2 API.
    /// </summary>
    /// <remarks>
    /// The API returns an RFC-7807-style envelope: <c>{type, code, message, param, doc_url}</c>.
    /// Surfacing those fields typed -- rather than as a flat message string -- is what lets calling
    /// code branch on <see cref="Code"/> and point a user at <see cref="DocUrl"/>.
    ///
    /// Distinct from <see cref="OWTransportError"/>: this means the server answered and said no.
    /// A DNS failure or a dropped connection is a different problem with different remedies, and
    /// conflating them makes "offline" indistinguishable from "your key is wrong".
    ///
    /// Not sealed: a write that became a proposal throws the one subclass, <see cref="OWProposedError"/>.
    /// </remarks>
    public class OWApiError : Exception
    {
        /// <summary>HTTP status code.</summary>
        public HttpStatusCode Status { get; }

        /// <summary>Numeric HTTP status, for callers that prefer the raw int.</summary>
        public int StatusCode => (int)Status;

        /// <summary>Broad error class, e.g. <c>invalid_request</c>.</summary>
        public string Type { get; }

        /// <summary>Specific machine-readable code, e.g. <c>invalid_link</c>.</summary>
        public string Code { get; }

        /// <summary>The offending field, when the error is about one.</summary>
        public string Param { get; }

        /// <summary>Documentation link for this error code.</summary>
        public string DocUrl { get; }

        /// <summary>The raw response body, for diagnostics the envelope did not model.</summary>
        public string RawBody { get; }

        public bool IsValidationError => StatusCode == 422 || StatusCode == 400;

        /// <summary>
        /// The key or PIN is wrong, unknown or revoked (<c>invalid_credentials</c>, <c>key_revoked</c>;
        /// <c>world_gone</c> is reserved). By code, as the npm SDK reads it: a 403 such as
        /// <c>not_author</c> or <c>owner_only</c> comes with a valid key, and checking the key
        /// will not help.
        /// </summary>
        public bool IsAuthError => Code == "invalid_credentials" || Code == "key_revoked" || Code == "world_gone";

        /// <summary>404 <c>not_found</c>: no such element or route. A 404 without the code (a proxy's) is not this.</summary>
        public bool IsNotFound => StatusCode == 404 && Code == "not_found";

        /// <summary>
        /// A party link's pick is played already (422 <c>character_taken</c>, param <c>character</c>): read the
        /// preview again and pick another. Nothing was spent.
        /// </summary>
        public bool IsCharacterTaken => StatusCode == 422 && Code == "character_taken";

        /// <summary>
        /// The pick is missing, not on the link's list, hidden or deleted since, or sent to a table that lists no
        /// Characters (422 <c>invalid_request</c>, param <c>character</c>). Nothing was spent.
        /// </summary>
        public bool IsCharacterInvalid => StatusCode == 422 && Code == "invalid_request" && Param == "character";

        /// <summary>The world has its 50 players (422 <c>world_full</c>), whichever player link was used.</summary>
        public bool IsWorldFull => StatusCode == 422 && Code == "world_full";

        /// <summary>
        /// A delete named a Character someone plays on the roster: the world owner's Character or a member's
        /// (422 <c>character_played</c>, param <c>id</c>). The message says who plays it. Nothing was
        /// deleted: re-point that member to another Character, or remove the member, then delete.
        /// </summary>
        public bool IsCharacterPlayed => StatusCode == 422 && Code == "character_played";

        /// <summary>
        /// An element write carried <c>known_of</c> (422 <c>known_of_not_writable</c>, param <c>known_of</c>),
        /// true or false: the key marks a stub and is never written. This client never sends
        /// it (<see cref="OWPayload.ReadOnlyFields"/> strips it, and a stub is refused before sending), so it
        /// reaches you only from a body sent some other way. <c>/bulk/</c> never throws for it: the item's
        /// slot carries the code, and its siblings apply.
        /// </summary>
        public bool IsKnownOfNotWritable => StatusCode == 422 && Code == "known_of_not_writable";

        /// <summary>
        /// An element write carried <c>x_served_trimmed</c> (422 <c>served_trimmed_not_writable</c>), the server's mark
        /// on a body it served with ids the reader's key may not see left out. The mark is never written, and this
        /// client sends a body as it was read, so the refusal is the signal that a trimmed body is being written back.
        /// Drop the mark and send again: a trimmed list sent back loses nobody, because the server keeps the ids the
        /// writer cannot see.
        /// </summary>
        public bool IsServedTrimmedNotWritable => StatusCode == 422 && Code == "served_trimmed_not_writable";

        /// <summary>
        /// A contributor or guest key changed, relinked or deleted an element someone else created
        /// (403 <c>not_author</c>). Retrying will not help.
        /// </summary>
        public bool IsNotAuthor => StatusCode == 403 && Code == "not_author";

        /// <summary>A member key tried what only the world's owner may do (403 <c>owner_only</c>).</summary>
        public bool IsOwnerOnly => StatusCode == 403 && Code == "owner_only";

        /// <summary>
        /// <c>/changes</c> refused the cursor (409 <c>resync_required</c>): a guest key's view of the
        /// world changed, or the key's role did. Walk the feed again from the start and REPLACE the
        /// local copy: a guest never receives deletes, so only a replace drops what it no longer sees.
        /// <see cref="OWSync.IncrementalAsync"/> does this itself.
        /// </summary>
        public bool IsResyncRequired => StatusCode == 409 && Code == "resync_required";

        /// <summary>
        /// Why <c>/changes</c> refused the cursor, from the 409 <c>resync_required</c> envelope's <c>reason</c> (only
        /// while keel's hidden mark is on): <c>role</c> (this key's own role or membership moved: keep what the
        /// local copy holds that the new walk does not name; it is out of view now, not gone) or <c>view</c> (something
        /// left this key's view: drop what the new walk does not name). When both happened since the cursor, keel says
        /// <c>role</c>. Null for any other error, or when keel sends no reason (then keep the copy: a reset never empties
        /// one).
        /// </summary>
        public string ResyncReason
        {
            get
            {
                if (!IsResyncRequired || string.IsNullOrEmpty(RawBody)) return null;

                try
                {
                    var reason = (JObject.Parse(RawBody)["error"] as JObject)?["reason"] as JValue;
                    var text = reason?.Type == JTokenType.String ? (string)reason : null;
                    return text == "role" || text == "view" ? text : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// A conditional write's <c>If-Match</c> was stale (412 <c>precondition_failed</c>: a patch, a link edit or a delete): the
        /// element changed since the <c>change_seq</c> you sent, and nothing was written. <see cref="Current"/> is
        /// the element as it is now; decide from it, then send again with its <c>change_seq</c>.
        /// </summary>
        public bool IsPreconditionFailed => StatusCode == 412 && Code == "precondition_failed";

        /// <summary>
        /// The <c>current</c> object beside the envelope of a 412 (<see cref="IsPreconditionFailed"/>) or a 409
        /// <c>stale_proposal</c> (<see cref="IsStaleProposal"/>): the element as the server holds it now. Null on any
        /// other answer, when keel sent none, or when the body is not JSON.
        /// </summary>
        public JObject Current
            => StatusCode == 412 || IsStaleProposal ? BesideTheEnvelope()?["current"] as JObject : null;

        /// <summary>
        /// On a 409 <c>stale_proposal</c> (<see cref="IsStaleProposal"/>): each proposed field that moved since the
        /// proposal was made, with its value then and now (npm's <c>OwStaleProposalError.conflicts</c>). Empty on any
        /// other error, or when keel sent none. A change to a field the proposal does not name does not count.
        /// </summary>
        public IReadOnlyList<OWProposalConflict> Conflicts
        {
            get
            {
                var list = new List<OWProposalConflict>();
                if (!IsStaleProposal || !(BesideTheEnvelope()?["conflicts"] is JArray items)) return list;

                foreach (var item in items)
                {
                    if (item is JObject o && o["field"]?.Type == JTokenType.String)
                    {
                        list.Add(new OWProposalConflict { Field = (string)o["field"], Base = o["base"], Current = o["current"] });
                    }
                }

                return list;
            }
        }

        /// <summary>The whole answer as an object (the envelope and what keel sends beside it), or null when it is not JSON.</summary>
        private JObject BesideTheEnvelope()
        {
            if (string.IsNullOrEmpty(RawBody)) return null;

            try
            {
                return JObject.Parse(RawBody);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// A patch or a link edit became a proposal (202): this error is an <see cref="OWProposedError"/>,
        /// whose <see cref="OWProposedError.Proposal"/> is the row a keeper decides. The element did not change. Not a
        /// failure to retry: the write is waiting for a person.
        /// </summary>
        public bool IsProposed => StatusCode == 202 && this is OWProposedError;

        /// <summary>
        /// A proposal named a field the proposer's own view of the element does not hold (422 <c>field_not_visible</c>,
        /// <see cref="Param"/> names it): the element reaches this key only as a stub, and a proposal changes
        /// only what its proposer can read.
        /// </summary>
        public bool IsFieldNotVisible => StatusCode == 422 && Code == "field_not_visible";

        /// <summary>
        /// This proposer already has 5 open proposals on the element, or 50 in the world (422 <c>too_many_open</c>).
        /// Wait for a keeper to decide, or withdraw one; a lapsed proposal no longer counts.
        /// </summary>
        public bool IsTooManyOpen => StatusCode == 422 && Code == "too_many_open";

        /// <summary>
        /// An accept found a proposed field moved since the proposal was made (409 <c>stale_proposal</c>).
        /// Nothing was written and the proposal stays open; <see cref="Conflicts"/> lists each moved field and
        /// <see cref="Current"/> is the element as it is now. The proposer proposes again on the current text.
        /// </summary>
        public bool IsStaleProposal => StatusCode == 409 && Code == "stale_proposal";

        /// <summary>
        /// While the world takes agent seats' writes as proposals, an agent seat's key tried what it may not:
        /// delete an element it did not create, or accept or decline a proposal (403 <c>proposed_only</c>). On any world,
        /// a review of a new element by the member its mark names is the same 403. Retrying will not help; a
        /// person decides.
        /// </summary>
        public bool IsProposedOnly => StatusCode == 403 && Code == "proposed_only";

        /// <summary>
        /// An accept or decline of the key's own proposal (403 <c>own_proposal</c>): another keeper (the owner
        /// or a co-builder) decides it. Retrying will not help.
        /// </summary>
        public bool IsOwnProposal => StatusCode == 403 && Code == "own_proposal";

        /// <summary>
        /// A review of a new element that carries no <c>x_review_proposed</c> mark (422 <c>not_proposed</c>):
        /// nothing to settle. It was never written as proposed, or an accept already landed (a retried accept meets
        /// this; a retried decline meets a 404). npm's <c>isNotProposed</c>, the Python package's <c>is_not_proposed</c>.
        /// </summary>
        public bool IsNotProposed => StatusCode == 422 && Code == "not_proposed";

        /// <summary>
        /// Seconds to wait before retrying, from the <c>Retry-After</c> header (a delay in seconds,
        /// or an HTTP date turned into one), else null. keel sends it on 503 <c>server_busy</c>,
        /// 429 <c>rate_limited</c> and <c>quota_exceeded</c>, and 409 <c>idempotency_in_flight</c>.
        /// This client never retries on its own; the value is here so callers can back off politely.
        /// </summary>
        public int? RetryAfter { get; }

        /// <summary>
        /// True when sending the same request again, after <see cref="RetryAfter"/> when there is
        /// one, could plausibly succeed soon: a server fault, a busy server (503 <c>server_busy</c>),
        /// a PIN lockout (429 <c>rate_limited</c>), or a same-key request still running (409
        /// <c>idempotency_in_flight</c>: send the same key and body again, and it replays the first
        /// one's answer; the image routes' 409 <c>upload_pending</c> and <c>removal_in_flight</c>
        /// likewise). False for a daily allowance used up (429 <c>quota_exceeded</c>, which
        /// lasts until midnight UTC), for a feature switched off (503 <c>ask_unavailable</c>,
        /// <c>media_unavailable</c>), and for every other 4xx, which fails again identically.
        /// </summary>
        public bool IsRetryable
        {
            get
            {
                switch (Code)
                {
                    case "idempotency_in_flight":
                    case "upload_pending":
                    case "removal_in_flight":
                        return StatusCode == 409;
                    case "quota_exceeded":
                    case "ask_unavailable":
                    case "media_unavailable":
                        return false;
                    default:
                        return StatusCode == 429 || StatusCode >= 500;
                }
            }
        }

        public OWApiError(
            HttpStatusCode status,
            string message,
            string type = null,
            string code = null,
            string param = null,
            string docUrl = null,
            string rawBody = null,
            int? retryAfter = null)
            : base(BuildMessage(status, message, code, param))
        {
            Status = status;
            Type = type;
            Code = code;
            Param = param;
            DocUrl = docUrl;
            RawBody = rawBody;
            RetryAfter = retryAfter;
        }

        /// <summary>
        /// <c>Retry-After</c> as seconds: a non-negative delay, or an HTTP date turned into one; else
        /// null. As the npm SDK's <c>parseRetryAfter</c>: only a value with a letter in it is read as
        /// a date, so <c>"1.5"</c>, <c>"-1"</c> or <c>"+5"</c> is null, never "retry now".
        /// </summary>
        public static int? ParseRetryAfter(string value, DateTimeOffset now)
        {
            if (value == null) return null;
            var v = value.Trim();
            if (v.Length == 0) return null;

            var digits = true;
            var letter = false;
            foreach (var c in v)
            {
                if (c < '0' || c > '9') digits = false;
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) letter = true;
            }

            if (digits) return int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : int.MaxValue;
            if (!letter) return null;

            if (!DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when))
            {
                return null;
            }

            var seconds = Math.Ceiling((when - now).TotalSeconds);
            return seconds <= 0 ? 0 : seconds >= int.MaxValue ? int.MaxValue : (int)seconds;
        }

        private static string BuildMessage(HttpStatusCode status, string message, string code, string param)
        {
            var text = $"OnlyWorlds API {(int)status}";
            if (!string.IsNullOrEmpty(code)) text += $" [{code}]";
            if (!string.IsNullOrEmpty(param)) text += $" (field: {param})";
            if (!string.IsNullOrEmpty(message)) text += $": {message}";
            return text;
        }
    }

    /// <summary>
    /// A network-layer failure: the request never got an answer.
    /// </summary>
    /// <remarks>
    /// Deliberately not an <see cref="OWApiError"/>. "The server rejected this" and "the server was
    /// unreachable" want different handling -- the first is a bug in the request, the second is a
    /// condition to retry or surface as offline.
    /// </remarks>
    public sealed class OWTransportError : Exception
    {
        public OWTransportError(string message, Exception inner = null) : base(message, inner) { }
    }
}
