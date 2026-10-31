using System;
using Newtonsoft.Json;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// <see cref="OWClient.CreateMediaTicketAsync"/>'s 201 (POST /media/ticket; npm's <c>OwMediaTicket</c>): a
    /// short-lived, single-use permission to upload one image into this world's prefix. keel never sees the bytes; the
    /// upload host at <see cref="UploadUrl"/> does.
    /// </summary>
    [Serializable]
    public class OWMediaTicket
    {
        /// <summary>The ticket, opaque: sent to <see cref="UploadUrl"/> as <c>Authorization: Bearer &lt;ticket&gt;</c>, as is. A credential: keep it out of logs.</summary>
        [JsonProperty("ticket")]
        public string Ticket;

        /// <summary>Where the image goes (<c>https://upload.onlyworlds.com/v1/upload</c> on onlyworlds.com).</summary>
        [JsonProperty("upload_url")]
        public string UploadUrl;

        /// <summary><c>u/&lt;world_id&gt;/</c>: every upload with this ticket lands under it, and a key given must start with it.</summary>
        [JsonProperty("prefix")]
        public string Prefix;

        /// <summary>The most this ticket accepts, in bytes: the per-image limit, or what the account has left, whichever is smaller.</summary>
        [JsonProperty("max_bytes")]
        public long MaxBytes;

        /// <summary>Unix seconds after which the upload host refuses the ticket.</summary>
        [JsonProperty("exp")]
        public long Exp;

        /// <summary>Always 1: one ticket, one upload.</summary>
        [JsonProperty("uses")]
        public int Uses;

        /// <summary>Who answers for the upload; the upload host records it with the stored object.</summary>
        [JsonProperty("issued_to")]
        public OWTicketIssuedTo IssuedTo;
    }

    /// <summary>
    /// Who answers for an upload, as a ticket names it: the owner's and classic keys give the owner's account and no
    /// membership, a member's key the member's account and membership, an agent seat its sponsor's account and the seat.
    /// </summary>
    [Serializable]
    public class OWTicketIssuedTo
    {
        [JsonProperty("account")]
        public string Account;

        [JsonProperty("membership")]
        public string Membership;
    }

    /// <summary>
    /// The upload host's 201 for one image (<see cref="OWClient.UploadImageAsync"/>; npm's <c>OwUploadedImage</c>). Put
    /// <see cref="Url"/> in an element's <c>image_url</c>.
    /// </summary>
    [Serializable]
    public class OWUploadedImage
    {
        /// <summary>The permanent public URL (<c>https://media.onlyworlds.com/&lt;key&gt;</c> on onlyworlds.com).</summary>
        [JsonProperty("url")]
        public string Url;

        /// <summary>The object key, under the ticket's <see cref="OWMediaTicket.Prefix"/>.</summary>
        [JsonProperty("key")]
        public string Key;

        [JsonProperty("bytes")]
        public long Bytes;

        /// <summary>The MIME type the upload host read from the bytes (never from a header or a file name).</summary>
        [JsonProperty("type")]
        public string Type;

        [JsonProperty("etag")]
        public string Etag;
    }

    /// <summary>
    /// <see cref="OWClient.CreateRemovalTicketAsync"/>'s 200 (POST /media/remove-ticket; npm's <c>OwRemovalTicket</c>): a
    /// short-lived, single-use permission to remove one image, which the upload host at <see cref="RemoveUrl"/> carries out.
    /// </summary>
    [Serializable]
    public class OWRemovalTicket
    {
        /// <summary>The ticket, opaque: sent to <see cref="RemoveUrl"/> as <c>Authorization: Bearer &lt;ticket&gt;</c>, as is. A credential: keep it out of logs.</summary>
        [JsonProperty("ticket")]
        public string Ticket;

        /// <summary>Where the removal goes: the upload host's POST /v1/remove (<c>https://upload.onlyworlds.com/v1/remove</c> on onlyworlds.com).</summary>
        [JsonProperty("remove_url")]
        public string RemoveUrl;

        /// <summary>Unix seconds after which the upload host refuses the ticket (10 minutes after issue).</summary>
        [JsonProperty("exp")]
        public long Exp;

        /// <summary>The object key the ticket removes.</summary>
        [JsonProperty("key")]
        public string Key;

        /// <summary>How many of the world's elements show the image. Removal does not change them: clear their <c>image_url</c> yourself.</summary>
        [JsonProperty("referenced")]
        public int Referenced;
    }

    /// <summary>The upload host's 200 for one removal (<see cref="OWClient.RemoveImageAsync"/>; npm's <c>OwRemovedImage</c>).</summary>
    [Serializable]
    public class OWRemovedImage
    {
        /// <summary>The object key that was removed.</summary>
        [JsonProperty("removed")]
        public string Removed;

        /// <summary>The bytes given back to the uploading account's storage.</summary>
        [JsonProperty("bytes")]
        public long Bytes;

        /// <summary>
        /// <c>may_linger</c>: copies the network or a browser already holds can keep answering for days or longer (images
        /// are cached as unchanging for a year). <c>purging</c>: the edge cache is being cleared too.
        /// </summary>
        [JsonProperty("cache")]
        public string Cache;
    }
}
