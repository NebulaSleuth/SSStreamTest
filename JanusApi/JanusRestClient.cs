//-----------------------------------------------------------------------------
// Filename: JanusRestClient.cs
//
// Description: Minimal client to connect to the Janus WebRTC Server's REST
// interface and establish an Echo WebRTC connection.
//
// The HTTP REST interface is defined at:
// https://janus.conf.meetecho.com/docs/rest.html
//
// The Echo plugin operations are defined at:
// https://janus.conf.meetecho.com/docs/echotest.html
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 04 Feb 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json;
using RestSharp;
using RestSharp.Extensions;
using RestSharp.Serializers.NewtonsoftJson;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;

namespace JanusApi
{
    public class JanusRestClient
    {
        private readonly string _serverUrl;
        private ulong _sessionID;
        private CancellationToken _ct;
        private Microsoft.Extensions.Logging.ILogger _logger;

        public event Action<string> OnJanusEvent;

        public JanusRestClient(string serverURL,
            Microsoft.Extensions.Logging.ILogger logger,
            CancellationToken ct)
        {
            _serverUrl = serverURL;
            _ct = ct;
            _logger = logger;
        }

        /// <summary>
        /// Gets generic server properties from the Janus instance.
        /// </summary>
        public async Task<ServerInfo> GetServerInfo()
        {
            _logger.LogDebug("Creating Janus session...");

            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            var infoReq = new RestRequest("info", DataFormat.Json);
            return await client.GetAsync<ServerInfo>(infoReq, _ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Starts a new session with the Janus server. A session is required for 
        /// most operations including being able to use a plugin to create a WebRTC
        /// session.
        /// </summary>
        public async Task StartSession()
        {
            _sessionID = await CreateSession(_ct).ConfigureAwait(false);

            if (_sessionID == 0)
            {
                throw new ApplicationException("Janus session creation failed.");
            }
            else
            {
                _ = Task.Factory.StartNew(StartLongPoll, TaskCreationOptions.LongRunning);
            }
        }

        /// <summary>
        /// Creates a new session with the Echo plugin. This will request Janus to create a 
        /// new WebRTC session. The SDP answer will be supplied as a response on the HTTP long
        /// poll connection.
        /// </summary>
        /// <param name="sdpOffer">The SDP offer from a the WebRTC peer that wants to connect
        /// to the Echo plugin.</param>
        public async Task StartEcho(string sdpOffer)
        {
            var pluginID = await AttachPlugin(JanusPlugins.ECHO_TEST).ConfigureAwait(false);

            if (pluginID == 0)
            {
                throw new ApplicationException("Janus session failed to create echo plugin.");
            }
            else
            {
                await StartEcho(pluginID, sdpOffer).ConfigureAwait(false);
            }
        }

        public async Task<ulong> StartVideoRoomPlugin()
        {
            var pluginID = await AttachPlugin(JanusPlugins.VIDEO_ROOM).ConfigureAwait(false);

            if (pluginID == 0)
            {
                throw new ApplicationException("Janus session failed to create echo plugin.");
            }

            return pluginID;
        }

        public async Task<ulong> StartStreamingPlugin()
        {
            var pluginID = await AttachPlugin(JanusPlugins.STREAMING).ConfigureAwait(false);

            if (pluginID == 0)
            {
                throw new ApplicationException("Janus session failed to create echo plugin.");
            }

            return pluginID;
        }

        public async Task<bool> RoomExists(ulong pluginId, int roomId)
        {
            var rooms = await ListRooms(pluginId);
            var roomData = rooms.plugindata.data;
            if (roomData?.list?.Count > 0)
            {
                foreach (var room in roomData.list)
                {
                    if (room.room == roomId)
                    {
                        return true;
                    }   
                }
            }
            return false;
        }
        public async Task<dynamic> ListRooms(ulong pluginId)
        {
            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            dynamic obj = new ExpandoObject();
            obj.request = JanusVideoRoomRequestTypes.LIST;
            var msg = new JanusRequest { janus = JanusOperationsEnum.message, body = obj };

            //if (api_secret.HasValue()) msg.apisecret = api_secret;

            //var requestBody = new JanusVideoRoomListReq();
            _logger.LogDebug(JsonConvert.SerializeObject(msg));
            var request = new RestRequest($"{_sessionID}/{pluginId}", Method.POST, DataFormat.Json);
            request.AddJsonBody(msg);
            var response = await client.ExecutePostAsync<dynamic>(request, _ct).ConfigureAwait(false);

            return response.Data;

        }

        public async Task<dynamic> ListParticipants(ulong pluginId, int roomId)
        {
            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            dynamic obj = new ExpandoObject();
            obj.request = JanusVideoRoomRequestTypes.LISTPARTICIPANTS;
            obj.room = roomId;  
            var msg = new JanusRequest { janus = JanusOperationsEnum.message, body = obj };

            //if (api_secret.HasValue()) msg.apisecret = api_secret;

            //var requestBody = new JanusVideoRoomListReq();
            _logger.LogDebug(JsonConvert.SerializeObject(msg));
            var request = new RestRequest($"{_sessionID}/{pluginId}", Method.POST, DataFormat.Json);
            request.AddJsonBody(msg);
            var response = await client.ExecutePostAsync<dynamic>(request, _ct).ConfigureAwait(false);

            return response.Data;

        }

        public async Task<dynamic> CreateRoom(ulong pluginId, int roomId, string _description = null, string _secret = null, int _bitrate = 0, string _publishers = null, bool _record = false, string _rec_dir = null, int _fir_freq = 0, bool _private = false)
        {
            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            dynamic obj = new ExpandoObject();
            obj.request = JanusVideoRoomRequestTypes.CREATE;
            obj.room = roomId;
            obj.record = _record;
            obj.is_private = _private;
            obj.videocodec = "vp9,h264,vp8";
            obj.h264_profile = "42e01f";
            if (_bitrate > 0)
            {
                obj.bitrate = _bitrate;
            }
            if (_fir_freq > 0)
            {
                obj.fir_freq = _fir_freq;
            }
            if (_description.HasValue())
            {
                obj.description = _description;
            }
            if (_secret.HasValue())
            {
                obj.secret = _secret;
            }
            if (_rec_dir.HasValue())
            {
                obj.rec_dir = _rec_dir;
            }
            var msg = new JanusRequest { janus = JanusOperationsEnum.message, body = obj };

            //var requestBody = new JanusVideoRoomCreateReq(roomId, false, _description, _secret, null, _bitrate, _publishers, _record, _rec_dir, _fir_freq, _private);
            _logger.LogDebug(JsonConvert.SerializeObject(msg));
            var request = new RestRequest($"{_sessionID}/{pluginId}", Method.POST, DataFormat.Json);
            request.AddJsonBody(msg);
            var response = await client.ExecutePostAsync<dynamic>(request, _ct).ConfigureAwait(false);

            return response.Data;

        }

        public async Task<dynamic> RemoveRoom(ulong pluginId, int roomId, string _secret = null)
        {
            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            dynamic obj = new ExpandoObject();
            obj.request = JanusVideoRoomRequestTypes.DESTROY;
            obj.room = roomId;
            if (_secret.HasValue())
            {
                obj.secret = _secret;
            }

            var msg = new JanusRequest { janus = JanusOperationsEnum.message, body = obj };

            Console.WriteLine(JsonConvert.SerializeObject(msg));

            var request = new RestRequest($"{_sessionID}/{pluginId}", Method.POST, DataFormat.Json);
            request.AddJsonBody(msg);
            var response = await client.ExecutePostAsync<dynamic>(request, _ct).ConfigureAwait(false);

            return response.Data;

        }


        public async Task<dynamic> Unpublish(ulong pluginId)
        {
            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            dynamic obj = new ExpandoObject();
            obj.request = JanusVideoRoomRequestTypes.UNPUBLISH;

            var msg = new JanusRequest { janus = JanusOperationsEnum.message, body = obj };

            Console.WriteLine(JsonConvert.SerializeObject(msg));

            var request = new RestRequest($"{_sessionID}/{pluginId}", Method.POST, DataFormat.Json);
            request.AddJsonBody(msg);
            var response = await client.ExecutePostAsync<dynamic>(request, _ct).ConfigureAwait(false);

            return response.Data;

        }

        public async Task<dynamic> Leave(ulong pluginId)
        {
            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            dynamic obj = new ExpandoObject();
            obj.request = JanusVideoRoomRequestTypes.LEAVE;

            var msg = new JanusRequest { janus = JanusOperationsEnum.message, body = obj };

            Console.WriteLine(JsonConvert.SerializeObject(msg));

            var request = new RestRequest($"{_sessionID}/{pluginId}", Method.POST, DataFormat.Json);
            request.AddJsonBody(msg);
            var response = await client.ExecutePostAsync<dynamic>(request, _ct).ConfigureAwait(false);

            return response.Data;

        }

        public async Task<dynamic> Subscribe(ulong pluginId, int roomId, int id)
        {
            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            dynamic obj = new ExpandoObject();
            List<dynamic> feeds = new List<dynamic>();
            feeds.Add(new { feed = id });
            obj.request = JanusVideoRoomRequestTypes.JOIN;
            obj.ptype = "subscriber";
            obj.room = roomId;
            obj.streams = feeds;

            var msg = new JanusRequest { janus = JanusOperationsEnum.message, body = obj };

            Console.WriteLine(JsonConvert.SerializeObject(msg));

            var request = new RestRequest($"{_sessionID}/{pluginId}", Method.POST, DataFormat.Json);
            request.AddJsonBody(msg);
            var response = await client.ExecutePostAsync<dynamic>(request, _ct).ConfigureAwait(false);

            return response.Data;

        }

        public async Task<dynamic> Watch(ulong pluginId, int streamId, string videoMid, string audioMid)
        {
            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            dynamic obj = new ExpandoObject();
            obj.request = JanusVideoRoomRequestTypes.WATCH;
            obj.id = streamId;
            List<string> streams = new List<string>();
            if (string.IsNullOrEmpty(videoMid) == false)
            {
                streams.Add(videoMid);
            }
            if (string.IsNullOrEmpty(audioMid) == false)
            {
                streams.Add(audioMid);
            }
            obj.offer_video = true;

            var msg = new JanusRequest { janus = JanusOperationsEnum.message, body = obj };

            Console.WriteLine(JsonConvert.SerializeObject(msg));

            var request = new RestRequest($"{_sessionID}/{pluginId}", Method.POST, DataFormat.Json);
            request.AddJsonBody(msg);
            var response = await client.ExecutePostAsync<dynamic>(request, _ct).ConfigureAwait(false);

            return response.Data;

        }


        public async Task<dynamic> JoinRoom(ulong pluginId, int roomId, string type, int id, string name)
        {
            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            //var requestBody = new JanusVideoRoomJoinReq(roomId, id, type, name);

            dynamic obj = new ExpandoObject();
            obj.request = JanusVideoRoomRequestTypes.JOIN;
            obj.room = roomId;
            obj.ptype = type;
            obj.display = name;
            obj.id = id;

            var msg = new JanusRequest { janus = JanusOperationsEnum.message, body = obj };

            _logger.LogDebug(JsonConvert.SerializeObject(msg));
            var request = new RestRequest($"{_sessionID}/{pluginId}", Method.POST, DataFormat.Json);
            request.AddJsonBody(msg);
            var response = await client.ExecutePostAsync<dynamic>(request, _ct).ConfigureAwait(false);

            return response.Data;

        }

        /// <summary>
        /// Closes and destroys a Janus session allowing the server to free any
        /// resources or plugins attached to it.
        /// </summary>
        public async Task DestroySession()
        {
            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            var destroyReqBody = new JanusRequest { janus = JanusOperationsEnum.destroy };
            var destroyReq = new RestRequest(_sessionID.ToString(), Method.POST, DataFormat.Json);
            destroyReq.AddJsonBody(destroyReqBody);
            var destroyResp = await client.ExecutePostAsync<JanusResponse>(destroyReq).ConfigureAwait(false);

            _logger.LogDebug($"Destroy response: {destroyResp.Data.janus}.");
        }

        public async Task<dynamic> SendStart(ulong pluginId, string sdp)
        { 
            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            //var requestBody = new JanusVideoRoomJoinReq(roomId, id, type, name);

            dynamic obj = new ExpandoObject();
            obj.request = JanusVideoRoomRequestTypes.START;

            var msg = new JanusRequest { janus = JanusOperationsEnum.message, body = obj, jsep = new Jsep { type = "answer", sdp = sdp } };

            _logger.LogDebug(JsonConvert.SerializeObject(msg));
            var request = new RestRequest($"{_sessionID}/{pluginId}", Method.POST, DataFormat.Json);
            request.AddJsonBody(msg);
            var response = await client.ExecutePostAsync<dynamic>(request, _ct).ConfigureAwait(false);

            return response.Data;

        }

        public async Task<dynamic> CreateStream(int id, string name)
        {
            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            var pluginId = await AttachPlugin(JanusPlugins.STREAMING).ConfigureAwait(false);

            //var requestBody = new JanusVideoRoomJoinReq(roomId, id, type, name);

            dynamic obj = new ExpandoObject();
            dynamic vid = new ExpandoObject();
            vid.type = "video";
            vid.mid = "video1";
            vid.codec = "vp8";
            vid.port = 8004;
            vid.pt = 100;
            vid.payload_type = 100;

            dynamic aud = new ExpandoObject();
            aud.type = "audio";
            aud.mid = "audio1";
            aud.codec = "opus";
            aud.port = 8005;
            aud.pt = 111;
            aud.payload_type = 111;

            obj.request = JanusVideoRoomRequestTypes.CREATE;
            obj.type = "rtp";
            obj.id = id;
            obj.name = name;
            obj.description = name + " STREAM";
            obj.is_private = false;
            obj.media = new dynamic[]
            {
                vid,
                aud
            };
            obj.permanent = true;

            var msg = new JanusRequest { janus = JanusOperationsEnum.message, body = obj};

            _logger.LogDebug(JsonConvert.SerializeObject(msg));
            var request = new RestRequest($"{_sessionID}/{pluginId}", Method.POST, DataFormat.Json);
            request.AddJsonBody(msg);
            var response = await client.ExecutePostAsync<dynamic>(request, _ct).ConfigureAwait(false);

            await DetachPlugin(pluginId);

            return response.Data;

        }


        /// <summary>
        /// Attempts to create a new Janus session. This is the first step to do anything
        /// with the Janus WebRTC or other features.
        /// </summary>
        /// <returns>A non-zero session ID. A zero value indicates a failure.</returns>
        private async Task<ulong> CreateSession(CancellationToken ct)
        {
            _logger.LogDebug("Creating Janus session...");

            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            // Create session.
            var createSessionReq = new JanusRequest { janus = JanusOperationsEnum.create };
            var sessReq = new RestRequest(string.Empty, Method.POST, DataFormat.Json);
            sessReq.AddJsonBody(createSessionReq);
            var sessResp = await client.ExecutePostAsync<JanusResponse>(sessReq, ct).ConfigureAwait(false);

            ulong sessionID = sessResp.Data.data.id;

            _logger.LogDebug($"Result={sessResp.Data.janus}.");
            _logger.LogDebug($"Transaction={sessResp.Data.transaction}.");
            _logger.LogDebug($"SessionID={sessionID}.");

            return sessionID;
        }

        /// <summary>
        /// Janus requires a HTTP long poll mechanism to let it know that the client is still active.
        /// In addition any session events or responses to async REST requests will be provided as
        /// responses to the long poll GET request.
        /// </summary>
        private async Task StartLongPoll()
        {
            try
            {
                var longPollClient = new RestClient(_serverUrl);
                //longPollClient.UseNewtonsoftJson();

                while (!_ct.IsCancellationRequested)
                {
                    var getEventReq = new RestRequest(_sessionID.ToString());

                    _logger.LogDebug($"Sending long poll GET to {_serverUrl}{_sessionID}.");
                    var str = await longPollClient.GetAsync<string>(getEventReq, _ct);
                    if (!string.IsNullOrEmpty(str))
                    {
                        var getEventResp = JsonConvert.DeserializeObject<JanusResponse>(str);
                        _logger.LogDebug($"get event result={getEventResp.janus}.");

                        if (getEventResp.JanusOp != JanusOperationsEnum.keepalive)
                        {
                            OnJanusEvent?.Invoke(str);
                        }
                    }
                    await Task.Delay(1000);
                }
            }
            catch (TaskCanceledException)
            { }
            catch (Exception excp)
            {
                _logger.LogError(excp.Message);
            }

            _logger.LogDebug("Long poll thread exiting.");
        }

        /// <summary>
        /// Requests Janus to attach a new instance of a plugin to the current session.
        /// </summary>
        /// <param name="pluginType">The string ID of the plugin to attach.</param>
        /// <returns>The ID of the plugin session. A zero value indicates a failure.</returns>
        private async Task<ulong> AttachPlugin(string pluginType)
        {
            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            _logger.LogDebug($"Sending attach to {pluginType}.");
            var attachPluginReqBody = new AttachPluginRequest(pluginType);
            _logger.LogDebug(JsonConvert.SerializeObject(attachPluginReqBody));
            var attachReq = new RestRequest(_sessionID.ToString(), Method.POST, DataFormat.Json);
            attachReq.AddJsonBody(attachPluginReqBody);
            var attachResp = await client.ExecutePostAsync<JanusResponse>(attachReq, _ct).ConfigureAwait(false);

            _logger.LogDebug($"Attach response result={attachResp.Data.janus}.");
            _logger.LogDebug($"Attach response plugin id={attachResp.Data.data.id}.");

            return attachResp.Data.data.id;
        }

        public async Task DetachPlugin(ulong pluginId)
        {
            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            dynamic obj = new ExpandoObject();
            obj.request = JanusVideoRoomRequestTypes.DETACH;

            var msg = new JanusRequest { janus = JanusOperationsEnum.message, body = obj };

            _logger.LogDebug(JsonConvert.SerializeObject(msg));
            var request = new RestRequest($"{_sessionID}/{pluginId}", Method.POST, DataFormat.Json);
            request.AddJsonBody(msg);
            var response = await client.ExecutePostAsync<dynamic>(request, _ct).ConfigureAwait(false);

        }

        /// <summary>
        /// Sends the SDP offer to the Echo plugin instance which results in Janus starting the 
        /// WebRTC connection.
        /// </summary>
        /// <param name="echoPluginID">THe ID of the echo plugin instance.</param>
        /// <param name="offer">The WebRTC SDP offer to send to the Echo plugin.</param>
        private async Task StartEcho(ulong echoPluginID, string offer)
        {
            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            // Send SDP offer to janus streaming plugin.
            _logger.LogDebug("Send SDP offer to Janus echo test plugin.");
            var echoTestReqBody = new EchoTestRequest("offer", offer);
            //_logger.LogDebug(JsonConvert.SerializeObject(echoTestReqBody));
            var echoOfferReq = new RestRequest($"{_sessionID}/{echoPluginID}", Method.POST, DataFormat.Json);
            echoOfferReq.AddJsonBody(echoTestReqBody);
            var offerResp = await client.ExecutePostAsync<JanusResponse>(echoOfferReq, _ct).ConfigureAwait(false);

            if (offerResp.Data.JanusOp == JanusOperationsEnum.error)
            {
                var errResp = offerResp.Data;
                _logger.LogWarning($"Error, code={errResp.error.code}, reason={errResp.error.reason}.");
            }
            else
            {
                _logger.LogDebug($"Offer response result={offerResp.Data.janus}.");
            }
        }

        public async Task<dynamic> Publish(ulong pluginId, int id, string offer, string videocodec = null, string audiocodec = null, int bitrate = 0)
        {
            RestClient client = new RestClient(_serverUrl);
            client.UseNewtonsoftJson();

            dynamic obj = new ExpandoObject();
            obj.request = JanusVideoRoomRequestTypes.PUBLISH;
            if (videocodec != null)
            {
                obj.videocodec = videocodec;
            }
            if (audiocodec != null)
            {
                obj.audiocodec = audiocodec;
            }
            if (bitrate > 0)
            {
                obj.bitrate = bitrate;
            }

            var msg = new JanusRequest { janus = JanusOperationsEnum.message, body = obj, jsep = new Jsep { type = "offer", sdp = offer } };

            //var requestBody = new JanusVideoRoomPublishReq(id, offer);
            _logger.LogDebug(JsonConvert.SerializeObject(msg));
            var request = new RestRequest($"{_sessionID}/{pluginId}", Method.POST, DataFormat.Json);
            request.AddJsonBody(msg);
            var response = await client.ExecutePostAsync<dynamic>(request, _ct).ConfigureAwait(false);

            return response;

        }
    }
}
