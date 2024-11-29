using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RestSharp.Extensions;

namespace JanusApi
{
    public class JanusListRoomRequest
    {
        public string request { get; set; }
    }
    public class JanusVideoRoomListReq : JanusRequest
    {
        public JanusVideoRoomListReq()
        {
            body = new JanusListRoomRequest { request = JanusVideoRoomRequestTypes.LIST };
            janus = JanusOperationsEnum.message;
        }
    }
    public class JanusCreateRoomRequest
    {
        public string request { get; set; }
        public int room { get; set; }
        public bool permanent { get; set; }
        public string description { get; set; }
        public string secret { get; set; }
        public string pin { get; set; }
        public int bitrate { get; set; }
        public int fir_freq { get; set; }
        public bool record { get; set; }
        public string rec_dir { get; set; }
        public bool is_private { get; set; }
        public string[] allowed { get; set; }
    }

    public class JanusVideoRoomCreateReq : JanusRequest
    {
        public JanusVideoRoomCreateReq(int room, bool permanent, string description, string secret = null,
            string pin = null, int _bitrate = 0, string _publishers = null, bool _record = false,
            string _rec_dir = null, int _fir_freq = 0, bool is_private = false)
        {
            body = new JanusCreateRoomRequest
            {
                request = JanusVideoRoomRequestTypes.CREATE,
                room = room,
                permanent = permanent,
                description = description,
                secret = secret,
                pin = pin,
                is_private = is_private,
                record = _record,
                rec_dir = _rec_dir,
                allowed = _publishers?.Split(',').Select(x => x.Trim()).ToArray(),
                bitrate = _bitrate,
                fir_freq = _fir_freq
            };

            janus = JanusOperationsEnum.message;
        }
    }
    public class JanusJoinRoomRequest
    {
        public string request { get; set; }
        public string ptype { get; set; }
        public int room { get; set; }
        public int id { get; set; }
        public string display { get; set; }
        public string token { get; set; }

    }

    public class JanusVideoRoomJoinReq : JanusRequest
    {
        //public JanusJoinRoomRequest body { get; set; }
        public JanusVideoRoomJoinReq(int room, int id, string type, string name)
        {
            body = new JanusJoinRoomRequest
            {
                request = JanusVideoRoomRequestTypes.JOIN,
                room = room,
                ptype = type,
                display = name,
                id = id
            };

            janus = JanusOperationsEnum.message;
        }
    }

    public class JanusPublishRequest
    {
        public string request { get; set; }
        public string audiocodec { get; set; }
        public string videocodec { get; set; }
        public int bitrate { get; set; }
        public bool record { get; set; }
        public string filename { get; set; }
        public string display { get; set; }
    }

    public class JanusVideoRoomPublishReq : JanusRequest
    {
        //public Jsep jsep { get; set; }
        //public JanusPublishRequest body { get; set; }
        public JanusVideoRoomPublishReq(int room, string sdp, string videoCodec = null, string audioCodec = null, int bitrate = 0)
        {
            jsep = new Jsep
            {
                type = "offer",
                sdp = sdp,
            };
            body = new JanusPublishRequest
            {
                request = JanusVideoRoomRequestTypes.PUBLISH,
                videocodec = videoCodec,
                audiocodec = audioCodec,
                bitrate = bitrate,
            };

            janus = JanusOperationsEnum.message;
        }
    }
}
