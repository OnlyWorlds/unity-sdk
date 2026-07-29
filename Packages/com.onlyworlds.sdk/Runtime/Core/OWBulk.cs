using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>One element to write as part of a bulk request.</summary>
    public class OWBulkItem
    {
        [JsonProperty("type")]
        public string Type;

        [JsonProperty("element")]
        public JObject Element;

        public OWBulkItem() { }

        public OWBulkItem(string type, JObject element)
        {
            Type = type;
            Element = element;
        }
    }

    /// <summary>
    /// What the server did with one slot of a bulk request.
    /// </summary>
    /// <remarks>
    /// The per-slot <see cref="Status"/> is the only place the real outcome appears. The HTTP
    /// status of the whole request is 200 even when individual slots failed.
    /// </remarks>
    public class OWBulkSlot
    {
        [JsonProperty("status")]
        public int Status;

        [JsonProperty("id")]
        public string Id;

        [JsonProperty("created_at")]
        public string CreatedAt;

        [JsonProperty("updated_at")]
        public string UpdatedAt;

        /// <summary>Populated only on a failed slot.</summary>
        [JsonProperty("error")]
        public OWApiErrorBody Error;

        /// <summary>True for any 2xx slot.</summary>
        public bool Succeeded => Status >= 200 && Status < 300;
    }

    /// <summary>The error envelope carried by a failed bulk slot.</summary>
    public class OWApiErrorBody
    {
        [JsonProperty("type")]
        public string Type;

        [JsonProperty("code")]
        public string Code;

        [JsonProperty("message")]
        public string Message;

        [JsonProperty("param")]
        public string Param;

        [JsonProperty("doc_url")]
        public string DocUrl;

        public override string ToString()
            => string.IsNullOrEmpty(Param) ? $"{Code}: {Message}" : $"{Code} ({Param}): {Message}";
    }

    /// <summary>
    /// The result of a bulk write. <b>Partial failure is the normal case, not an exception.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// A bulk request returns HTTP 200 whether every slot succeeded or half of them failed, so a
    /// caller who checks only the transport status ships silent data loss. This type is shaped so
    /// that is hard to do: <see cref="Errors"/> is the server's own flag,
    /// <see cref="ThrowIfAnyFailed"/> converts partial failure into a real exception for callers
    /// who want that, and <see cref="Failed"/> hands back exactly the slots to look at.
    /// </para>
    /// <para>
    /// Slots come back in request order, so index <c>i</c> of <see cref="Items"/> corresponds to
    /// index <c>i</c> of what was sent.
    /// </para>
    /// </remarks>
    public class OWBulkResult
    {
        /// <summary>The server's own "something in here failed" flag.</summary>
        [JsonProperty("errors")]
        public bool Errors;

        /// <summary>One slot per submitted item, in request order.</summary>
        [JsonProperty("items")]
        public List<OWBulkSlot> Items = new List<OWBulkSlot>();

        /// <summary>
        /// True when the server replayed a previous identical request rather than performing it.
        /// </summary>
        /// <remarks>
        /// Read from the <c>idempotent-replay</c> response header, which arrives lowercase on the
        /// wire. Header lookup is case-insensitive, so this holds either way -- but the wire
        /// convention is lowercase and worth knowing when reading raw traffic.
        /// </remarks>
        public bool WasReplay;

        public IEnumerable<OWBulkSlot> Failed
        {
            get
            {
                foreach (var slot in Items)
                {
                    if (!slot.Succeeded) yield return slot;
                }
            }
        }

        public int SucceededCount
        {
            get
            {
                var n = 0;
                foreach (var slot in Items)
                {
                    if (slot.Succeeded) n++;
                }

                return n;
            }
        }

        /// <summary>
        /// Throws if any slot failed. For callers who would rather not handle partial success.
        /// </summary>
        /// <exception cref="OWBulkPartialFailureException">At least one slot failed.</exception>
        public void ThrowIfAnyFailed()
        {
            if (!Errors && SucceededCount == Items.Count) return;

            throw new OWBulkPartialFailureException(this);
        }
    }

    /// <summary>Raised by <see cref="OWBulkResult.ThrowIfAnyFailed"/>.</summary>
    public class OWBulkPartialFailureException : Exception
    {
        public OWBulkResult Result { get; }

        public OWBulkPartialFailureException(OWBulkResult result)
            : base(Describe(result))
        {
            Result = result;
        }

        private static string Describe(OWBulkResult result)
        {
            var failed = new List<string>();
            for (var i = 0; i < result.Items.Count; i++)
            {
                var slot = result.Items[i];
                if (slot.Succeeded) continue;

                failed.Add($"[{i}] {slot.Status} {slot.Error}");
            }

            return $"{failed.Count} of {result.Items.Count} bulk items failed: "
                   + string.Join("; ", failed);
        }
    }
}
