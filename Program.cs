using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;
using JanusApi;
using Newtonsoft.Json;
using static DirectShowLib.MediaSubType;
using static System.Net.Mime.MediaTypeNames;
using Application = System.Windows.Forms.Application;
using FFmpeg.AutoGen;

//
// Janus Stream configuration file
//
// ballcall: {
// 	type = "rtp"
// 	id = 1001
// 	description = "Ball Call Stream"
// 	metadata = "This is an example of a multistream mountpoint: you'll get an audio stream and two video feeds"
// 	media = (
// 		{
// 			type = "audio"
// 			mid = "audio1"
// 			label = "Audio stream"
// 			port = 8005
// 			pt = 111
// 			codec = "opus"
// 		},
// 		{
// 			type = "video"
// 			mid = "video1"
// 			label = "Video stream #1"
// 			port = 8004
// 			pt = 100
// 			codec = "vp8"
// 			videofmtp = "profile-level-id=42e01f;packetization-mode=1"
// 		}
// 	)
// }

//
// FFMPEG RTP command
//
// ffmpeg -f dshow -thread_queue_size 8192 -video_size 1280x720 -framerate 10 -i video="Astra Pro HD Camera" \
// -pix_fmt yuv420p -c:v libvpx -b:v 1M -deadline realtime -max_delay 0 -an -bufsize 1M -vsync 1 -g 10 -f rtp rtp://10.0.2.25:8004/
//

namespace demo
{
    class Program
    {
        private const string ffmpegLibFullPath = @"C:\ffmpeg\bin";


        private const string SERVER_URL = "http://10.0.2.25:8088/janus/";
        private static Microsoft.Extensions.Logging.ILogger logger = null;

        private static Form _form;
        private static PictureBox _picBox;

        static async Task Main(string[] args)
        {
            Console.WriteLine("WebRTC Client Test Console");
            CancellationTokenSource cts = new CancellationTokenSource();


            logger = AddConsoleLogger();
            try
            {
                var pc = new RTCPeerConnection(null);
                SIPSorceryMedia.FFmpeg.FFmpegInit.Initialise(SIPSorceryMedia.FFmpeg.FfmpegLogLevelEnum.AV_LOG_VERBOSE, ffmpegLibFullPath, logger);
                var videoSink = new FFmpegVideoEndPoint();
                videoSink.RestrictFormats(format => format.Codec == VideoCodecsEnum.VP8);

                pc.OnVideoFrameReceived += videoSink.GotVideoFrame;
                
                pc.OnVideoFormatsNegotiated += (formats) =>
                {
                    videoSink.SetVideoSourceFormat(formats.First());
                };
                pc.OnTimeout += (mediaType) => logger.LogDebug($"Peer connection timeout on media {mediaType}.");
                pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state changed to {state}.");
                pc.onconnectionstatechange += (state) =>
                {
                    logger.LogDebug($"Peer connection connected changed to {state}.");

                    if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.failed)
                    {
                    }
                };
                pc.OnClosed += () => logger.LogDebug($"Peer connection closed.");

                MediaStreamTrack videoTrack = new MediaStreamTrack(videoSink.GetVideoSinkFormats(), MediaStreamStatusEnum.RecvOnly);
                pc.addTrack(videoTrack);

                pc.OnVideoFrameReceived += videoSink.GotVideoFrame;
                pc.OnVideoFormatsNegotiated += (formats) =>
                    videoSink.SetVideoSinkFormat(formats.First());


                videoSink.OnVideoSinkDecodedSampleFaster += (RawImage rawImage) =>
                {
                    _form.BeginInvoke(new Action(() =>
                    {

                        if (rawImage.PixelFormat == SIPSorceryMedia.Abstractions.VideoPixelFormatsEnum.Rgb)
                        {
                            unsafe
                            {
                                Bitmap bmpImage = new Bitmap(rawImage.Width, rawImage.Height, rawImage.Stride, PixelFormat.Format24bppRgb, rawImage.Sample);
                                _picBox.Image = bmpImage;
                            }
                        }
                    }));
                };

                videoSink.OnVideoSinkDecodedSample += (byte[] bmp, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat) =>
                {
                    _form.BeginInvoke(new Action(() =>
                    {
                        unsafe
                        {
                            fixed (byte* s = bmp)
                            {
                                Bitmap bmpImage = new Bitmap((int)width, (int)height, (int)(bmp.Length / height), PixelFormat.Format24bppRgb, (IntPtr)s);
                                _picBox.Image = bmpImage;
                            }
                        }
                    }));
                };

                _form = new Form();
                _form.AutoSize = true;
                _form.BackgroundImageLayout = ImageLayout.Center;
                _picBox = new PictureBox
                {
                    Size = new Size(1280, 720),
                    Location = new Point(0, 0),
                    Visible = true
                };
                _form.Controls.Add(_picBox);


                cts = new CancellationTokenSource();

                ulong id = 0;

                // Connect to the janus server attach to the videoroom plugin and join stream 1001.  
                var janusClient = new JanusRestClient(SERVER_URL, logger, cts.Token);

                janusClient.OnJanusEvent += async (str) =>
                {
                    var resp = JsonConvert.DeserializeObject<dynamic>(str);
                    if (resp.jsep != null)
                    {
                        logger.LogDebug($"get event jsep={resp.jsep.type}.");

                        logger.LogDebug($"SDP Answer: {resp.jsep.sdp}");
                        var result = pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = resp.jsep.sdp });
                        logger.LogDebug($"SDP Answer: {pc.remoteDescription.sdp}");

                        if (result == SetDescriptionResultEnum.OK)
                        {
                            logger.LogDebug("Starting peer connection.");

                            var answerSdp = pc.createAnswer(null);
                            await pc.setLocalDescription(answerSdp);
                            await janusClient.SendStart(id, answerSdp.sdp);
                        }
                        else
                        {
                            logger.LogDebug($"Error setting remote SDP description {result}.");
                            pc.Close("error");
                        }
                    }
                    else
                    {
                        logger.LogDebug("Event: " + str);
                    }
                };

                await janusClient.StartSession().ConfigureAwait(false);
                id = await janusClient.StartStreamingPlugin().ConfigureAwait(false);
                var result = await janusClient.ListRooms(id).ConfigureAwait(false);
                if (result?.plugindata?.data?.list?.Count > 0)
                {
                    foreach (var stream in result.plugindata.data.list)
                    {
                        logger.LogDebug($"Stream: {stream.id} {stream.display}");
                        if (stream.id == 1001)
                        {
                            // Found it
                            await janusClient.Watch(id, (int)stream.id, "video1", null);

                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogDebug($"Exception WebRtcClient5.Connect. {excp.Message}");
            }

            Application.EnableVisualStyles();
            Application.Run(_form);
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var seriLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(seriLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}
