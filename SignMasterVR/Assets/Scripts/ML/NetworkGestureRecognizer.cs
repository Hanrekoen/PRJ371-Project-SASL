using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using SignMasterVR.Lessons;

namespace SignMasterVR.ML
{
    /// <summary>
    /// PHASE 15b — Real recognizer, backed by an external webcam on a laptop
    /// instead of the headset's own hand tracking. The laptop runs
    /// GestureServer/server.py (MediaPipe Hands + a classifier), and this
    /// component talks to it over a plain TCP socket on the local network.
    ///
    /// Wire format: newline-delimited JSON, one flat object per line. This is
    /// the SAME contract documented at the top of GestureServer/protocol.py —
    /// keep the two in sync, there is no shared schema between the languages.
    ///   We send  -> {"type":"target","gestureId":"A","level":1}
    ///   We get   <- {"type":"result","gestureId":"A","confidence":0.92}
    ///
    /// Swapping this in for FakeGestureRecognizer is the same "one-field
    /// change" IGestureRecognizer.cs already documents — done automatically by
    /// Tools > SignMasterVR > 13 Switch To Network Gesture Recognizer.
    ///
    /// All socket I/O runs on background threads (Unity's API is main-thread
    /// only); Update() drains a thread-safe queue so OnGestureDetected always
    /// fires on the main thread like every other MonoBehaviour event.
    /// </summary>
    public class NetworkGestureRecognizer : MonoBehaviour, IGestureRecognizer
    {
        [Header("Laptop connection")]
        [Tooltip("IP address of the laptop running GestureServer/server.py. On the laptop, run `ipconfig` (Windows) and use the IPv4 Address of the adapter on the SAME Wi-Fi/network as the headset. This can change if the laptop reconnects to Wi-Fi -- double check before each demo/playtest.")]
        public string laptopHost = "127.0.0.1";

        [Tooltip("Must match the --port the laptop's server.py was started with (default 8765).")]
        public int laptopPort = 8765;

        [Tooltip("Keep retrying the connection in the background if it drops, or hasn't been made yet.")]
        public bool autoReconnect = true;

        [Tooltip("Seconds to wait between reconnect attempts.")]
        public float reconnectIntervalSeconds = 3f;

        [Header("Optional")]
        [Tooltip("Only used so outgoing \"target\" messages can include the current level number, for the laptop's on-screen debug overlay. Not required for pass/fail logic -- that's still entirely LessonManager comparing GestureResult against GestureData, same as every other recognizer. Assigned automatically by Step 13.")]
        public LessonManager lessonManager;

        public event Action<GestureResult> OnGestureDetected;

        private TcpClient _client;
        private Thread _connectThread;
        private Thread _readThread;
        private readonly ConcurrentQueue<string> _incoming = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _outgoing = new ConcurrentQueue<string>();
        private volatile bool _running;
        private volatile bool _connected;
        private string _currentTarget;

        private void OnEnable()
        {
            _running = true;
            _connectThread = new Thread(ConnectLoop) { IsBackground = true, Name = "NetworkGestureRecognizer-Connect" };
            _connectThread.Start();
        }

        private void OnDisable()
        {
            _running = false;
            try { _client?.Close(); } catch { /* already closed/never opened */ }
        }

        private void OnDestroy()
        {
            _running = false;
            try { _client?.Close(); } catch { /* already closed/never opened */ }
        }

        // Runs on a background thread for the component's whole lifetime,
        // (re)connecting to the laptop whenever it isn't currently connected.
        private void ConnectLoop()
        {
            while (_running)
            {
                try
                {
                    _client = new TcpClient();
                    _client.Connect(laptopHost, laptopPort);
                    _connected = true;
                    LogMain($"Connected to laptop at {laptopHost}:{laptopPort}.");

                    // Re-send whatever gesture we're currently waiting on, in case
                    // this is a reconnect after the laptop program restarted.
                    if (_currentTarget != null) SendTarget(_currentTarget);

                    _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "NetworkGestureRecognizer-Read" };
                    _readThread.Start();

                    WriteLoop(); // blocks here, draining _outgoing, until the connection drops
                }
                catch (Exception e)
                {
                    if (_running) LogMainWarning($"Connection to {laptopHost}:{laptopPort} failed: {e.Message}");
                }
                finally
                {
                    _connected = false;
                    try { _client?.Close(); } catch { /* ignore */ }
                }

                if (!autoReconnect || !_running) break;
                Thread.Sleep(Mathf.Max(500, (int)(reconnectIntervalSeconds * 1000)));
            }
        }

        private void ReadLoop()
        {
            try
            {
                using (var stream = _client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    while (_running && _connected)
                    {
                        string line = reader.ReadLine();
                        if (line == null) break; // laptop closed the connection
                        if (line.Length > 0) _incoming.Enqueue(line);
                    }
                }
            }
            catch { /* connection dropped -- ConnectLoop's own catch/reconnect handles it */ }
            _connected = false;
        }

        // Drains _outgoing onto the socket. Blocks the calling thread (ConnectLoop)
        // until the connection dies, at which point ConnectLoop reconnects and
        // starts a fresh WriteLoop.
        private void WriteLoop()
        {
            using (var stream = _client.GetStream())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
            {
                while (_running && _connected)
                {
                    if (_outgoing.TryDequeue(out string line))
                    {
                        try { writer.WriteLine(line); }
                        catch { _connected = false; break; }
                    }
                    else
                    {
                        Thread.Sleep(20);
                    }
                }
            }
        }

        private void Update()
        {
            while (_incoming.TryDequeue(out string line))
                HandleMessage(line);
        }

        private void HandleMessage(string json)
        {
            WireMessage msg;
            try { msg = JsonUtility.FromJson<WireMessage>(json); }
            catch (Exception e)
            {
                Debug.LogWarning($"[NetworkGestureRecognizer] Bad message from laptop ({e.Message}): {json}");
                return;
            }
            if (msg == null || string.IsNullOrEmpty(msg.type)) return;

            switch (msg.type)
            {
                case "result":
                    OnGestureDetected?.Invoke(new GestureResult(msg.gestureId, msg.confidence));
                    break;
                case "hello_ack":
                    Debug.Log("[NetworkGestureRecognizer] Laptop handshake OK.");
                    break;
                case "pong":
                    break; // heartbeat reply, nothing to do
                default:
                    Debug.Log($"[NetworkGestureRecognizer] Unrecognized message type from laptop: \"{msg.type}\"");
                    break;
            }
        }

        public void SetTargetGesture(string gestureId)
        {
            _currentTarget = gestureId;
            SendTarget(gestureId);
        }

        private void SendTarget(string gestureId)
        {
            int level = 0;
            if (lessonManager != null && lessonManager.currentLevel != null)
                level = lessonManager.currentLevel.levelNumber;

            var msg = new WireMessage { type = "target", gestureId = gestureId, level = level };
            _outgoing.Enqueue(JsonUtility.ToJson(msg));
        }

        // Debug.Log is main-thread-only in some Unity versions when called from
        // a background thread it can throw/behave oddly during teardown -- route
        // connection-status logs through this each time instead of calling
        // Debug.Log directly from ConnectLoop/ReadLoop.
        private void LogMain(string message) => Debug.Log($"[NetworkGestureRecognizer] {message}");
        private void LogMainWarning(string message) => Debug.LogWarning($"[NetworkGestureRecognizer] {message}");

        [Serializable]
        private class WireMessage
        {
            public string type;
            public string gestureId;
            public float confidence;
            public int level;
        }
    }
}
