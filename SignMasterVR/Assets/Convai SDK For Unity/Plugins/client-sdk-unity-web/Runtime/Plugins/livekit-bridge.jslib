var NativeLib = {
    $LKBridge: {
        Data: null, // Map<number, object>
        Pointers: null, // Map<object, number>
        RefCount: null, // Map<number, number>
        Debug: false,
        RefIndex: 1,
        Stack: [],
        StackCSharp: [],
        FunctionInstance: null, // Current instance in a callback ( = this )
        NullPtr: 0,
        AudioTiming: null,

        EnsureAudioTimingContext: function () {
            var timing = LKBridge.AudioTiming;
            if (!timing) {
                timing = LKBridge.AudioTiming = {
                    context: null,
                    probes: new Map(),
                    nextId: 1
                };
            }

            try {
                if (!timing.context) {
                    var AudioContextClass = window.AudioContext || window.webkitAudioContext;
                    if (AudioContextClass) timing.context = new AudioContextClass();
                }
                if (timing.context && timing.context.state === 'suspended') timing.context.resume();
            } catch (error) {
                // Media-time clocking remains available when Web Audio is unavailable.
            }

            return timing;
        },

        EnsureAudioTimingAnalyser: function (probe) {
            var timing = LKBridge.AudioTiming;
            var context = timing && timing.context;
            if (!probe || !probe.element) return;

            var stream = probe.element.srcObject;
            var audioTracks = stream && typeof stream.getAudioTracks === 'function'
                ? stream.getAudioTracks()
                : [];
            var audioTrack = audioTracks.length > 0 ? audioTracks[0] : null;
            var sourceChanged = probe.analyser &&
                (probe.analyserStream !== stream ||
                 probe.analyserTrack !== audioTrack ||
                 !audioTrack ||
                 audioTrack.readyState === 'ended');
            if (sourceChanged) {
                try { probe.source.disconnect(); } catch (error) { }
                probe.source = null;
                probe.analyser = null;
                probe.analyserBuffer = null;
                probe.analyserStream = null;
                probe.analyserTrack = null;
                probe.analyserAvailable = false;
                probe.signalActive = false;
                probe.belowStopSinceMs = -1;
                probe.signalStart = -1;
                probe.discontinuityGeneration++;
            }
            if (probe.analyser ||
                !context ||
                !stream ||
                !audioTrack ||
                audioTrack.readyState === 'ended')
                return;

            try {
                probe.source = context.createMediaStreamSource(stream);
                probe.analyser = context.createAnalyser();
                // Keep enough waveform history to recover onset across a normal WebGL frame hitch.
                // Continuous monitoring below handles inter-response gaps; this larger window
                // protects the first monitor tick after a temporarily blocked browser main thread.
                probe.analyser.fftSize = 8192;
                probe.analyser.smoothingTimeConstant = 0;
                probe.analyserBuffer = new Float32Array(probe.analyser.fftSize);
                probe.source.connect(probe.analyser);
                probe.analyserStream = stream;
                probe.analyserTrack = audioTrack;
            } catch (error) {
                if (probe.source) {
                    try { probe.source.disconnect(); } catch (disconnectError) { }
                }
                probe.source = null;
                probe.analyser = null;
                probe.analyserBuffer = null;
                probe.analyserStream = null;
                probe.analyserTrack = null;
            }
        },

        UpdateAudioTimingProbe: function (probe) {
            var timing = LKBridge.AudioTiming;
            if (!timing || !probe || !probe.element) return;

            var now = performance.now();
            var raw = Number(probe.element.currentTime);
            if (!isFinite(raw) || raw < 0) raw = probe.lastRaw;
            var delta = raw - probe.lastRaw;
            var elapsed = Math.max(0, (now - probe.lastReadMs) / 1000);
            if (delta < -0.001) {
                probe.discontinuityGeneration++;
            } else if (delta > 0 && !probe.blocked) {
                if (delta > elapsed + 0.250) probe.discontinuityGeneration++;
                probe.logical += delta;
            } else if (delta > 0.001 && probe.blocked) {
                probe.discontinuityGeneration++;
            }
            probe.lastRaw = raw;
            probe.lastReadMs = now;

            if (probe.element.paused || probe.element.ended) {
                probe.blocked = true;
                probe.state = 0;
            } else if (probe.state !== 2) {
                probe.blocked = false;
                probe.state = 1;
            }

            var context = timing.context;
            LKBridge.EnsureAudioTimingAnalyser(probe);

            probe.analyserAvailable = !!(context && context.state === 'running' && probe.analyser);
            if (!probe.analyserAvailable) return;

            probe.analyser.getFloatTimeDomainData(probe.analyserBuffer);
            var firstStartFrame = -1;
            var aboveStop = false;
            for (var i = 0; i < probe.analyserBuffer.length; i++) {
                var magnitude = Math.abs(probe.analyserBuffer[i]);
                if (firstStartFrame < 0 && magnitude >= (500 / 32768)) firstStartFrame = i;
                if (magnitude >= (250 / 32768)) aboveStop = true;
            }

            if (!probe.signalActive && firstStartFrame >= 0) {
                var sampleRate = context.sampleRate > 0 ? context.sampleRate : 48000;
                var framesBehind = probe.analyserBuffer.length - 1 - firstStartFrame;
                probe.signalStart = Math.max(0, probe.logical - (framesBehind / sampleRate));
                probe.signalGeneration++;
                probe.signalActive = true;
                probe.belowStopSinceMs = -1;
            } else if (probe.signalActive) {
                if (aboveStop) {
                    probe.belowStopSinceMs = -1;
                } else if (probe.belowStopSinceMs < 0) {
                    probe.belowStopSinceMs = now;
                } else if (now - probe.belowStopSinceMs >= 500) {
                    probe.signalActive = false;
                    probe.belowStopSinceMs = -1;
                }
            }
        },

        StartAudioTimingMonitor: function (probe) {
            if (!probe || probe.monitoring) return;

            probe.monitoring = true;
            var monitor = function () {
                var timing = LKBridge.AudioTiming;
                if (!probe.monitoring || !timing || !timing.probes.has(probe.id)) return;

                LKBridge.UpdateAudioTimingProbe(probe);
                probe.monitorFrame = requestAnimationFrame(monitor);
            };
            probe.monitorFrame = requestAnimationFrame(monitor);
        },

        DynCall: function (sig, fnc, args) {
            if (typeof Runtime !== 'undefined' && typeof Runtime.dynCall === 'function') {
                return Runtime.dynCall(sig, fnc, args);
            }

            if (sig === 'vi') {
                return ({{{ makeDynCall('vi', 'fnc') }}}).apply(null, args);
            }

            var legacy = (typeof Module !== 'undefined') ? Module['dynCall_' + sig] : undefined;
            if (typeof legacy === 'function') {
                return legacy.apply(null, [fnc].concat(args));
            }

            var table = (typeof Module !== 'undefined' && Module['wasmTable']) ? Module['wasmTable'] : (typeof wasmTable !== 'undefined' ? wasmTable : undefined);
            if (table && typeof table.get === 'function') {
                return table.get(fnc).apply(null, args);
            }
        },

        NewRef: function () {
            var nPtr = LKBridge.RefIndex++;
            LKBridge.RefCount.set(nPtr, 0);
            LKBridge.SetRef(nPtr, null); // Set to null by default
            return nPtr;
        },

        FreeRef: function (ptr) {
            var obj = LKBridge.Data.get(ptr);
            LKBridge.Data.delete(ptr);
            LKBridge.RefCount.delete(ptr);
            LKBridge.Pointers.delete(obj);
        },

        SetRef: function (ptr, obj) {
            LKBridge.Data.set(ptr, obj);

            if (typeof obj === 'object' && obj !== null) {
                LKBridge.Pointers.set(obj, ptr);
            }
        },

        GetOrNewRef: function (obj) {
            var ptr = LKBridge.Pointers.get(obj);
            if (ptr === undefined || typeof obj !== 'object' || obj === null) {
                ptr = LKBridge.NewRef();
                LKBridge.SetRef(ptr, obj);
            }

            return ptr;
        },

        AddRef: function (ptr) {
            LKBridge.RefCount.set(ptr, LKBridge.RefCount.get(ptr) + 1);
            return ptr;
        },

        RemRef: function (ptr) {
            var count = LKBridge.RefCount.get(ptr) - 1;
            LKBridge.RefCount.set(ptr, count);

            if (count < 0) {
                console.error('LKBridge: The ref count of ' + ptr +  '(obj: ' + LKBridge.Data.get(ptr) + ') is negative ( Ptr management is wrong ! )');
            }
            
            if (count <= 0) {
                LKBridge.FreeRef(ptr);
            }

            return ptr;
        }
    },

    NewRef: function () {
        return LKBridge.AddRef(LKBridge.NewRef());
    },
    
    AddRef: function (ptr) {
        LKBridge.AddRef(ptr);
    },

    RemRef: function (ptr) {
        LKBridge.RemRef(ptr);
        return true;
    },
    
    SetRef: function(ptr){
        var value = LKBridge.Stack[0];
        LKBridge.Stack = [];
        LKBridge.SetRef(ptr, value);
    },

    InitLiveKit: function (debug) {
        // When initializing these variables directly, emscripten replace the type by {} (not sure why)
        LKBridge.Debug = debug === 1;
        LKBridge.Data = new Map();
        LKBridge.Pointers = new Map();
        LKBridge.RefCount = new Map();

        window.lkinternal = LKBridge;
    },

    GetProperty: function (ptr) {
        var key = LKBridge.Stack[0];
        LKBridge.Stack = [];

        var p = LKBridge.Data.get(ptr);
        var obj = p[key];
        
        return LKBridge.AddRef(LKBridge.GetOrNewRef(obj));
    },

    SetProperty: function (ptr) {
        var key = LKBridge.Stack[0];
        var value = LKBridge.Stack[1];
        LKBridge.Stack = [];

        var obj = LKBridge.Data.get(ptr);
        obj[key] = value;
    },

    IsNull: function (ptr) {
        return LKBridge.Data.get(ptr) === null;
    },

    IsUndefined: function (ptr) {
        return LKBridge.Data.get(ptr) === undefined;
    },

    IsString: function (ptr) {
        var obj = LKBridge.Data.get(ptr);
        return typeof obj === 'string' || obj instanceof String;
    },

    IsNumber: function (ptr) {
        var obj = LKBridge.Data.get(ptr);
        return typeof obj === 'number' && !isNaN(obj);
    },

    IsBoolean: function (ptr) {
        var obj = LKBridge.Data.get(ptr);
        return typeof obj === 'boolean';
    },

    IsObject: function (ptr) {
        var obj = LKBridge.Data.get(ptr);
        return typeof obj === 'object' && obj !== null;
    },

    PushNull: function () {
        LKBridge.Stack.push(null);
    },

    PushUndefined: function () {
        LKBridge.Stack.push(undefined);
    },

    PushNumber: function (nb) {
        LKBridge.Stack.push(nb);
    },

    PushBoolean: function (bool) {
        LKBridge.Stack.push(bool === 1);
    },

    PushString: function (str) {
        LKBridge.Stack.push(UTF8ToString(str));
    },

    PushStruct: function (json) {
        LKBridge.Stack.push(JSON.parse(UTF8ToString(json)));
    },

    PushData: function (data, offset, size) {
        var of = data + offset;
        LKBridge.Stack.push(HEAPU8.subarray(of, of + size));
    },

    PushFunction: function (ptr, fnc, labelPtr) {
        var label = UTF8ToString(labelPtr);
        LKBridge.Stack.push(function () {
            if (!LKBridge.Data.has(ptr)) {
                console.warn("Trying to fire an event on a freed object", ptr, fnc, label);
                return;
            } 
            
            try{
                LKBridge.StackCSharp = Array.from(arguments);
                LKBridge.FunctionInstance = this;

                LKBridge.DynCall('vi', fnc, [LKBridge.AddRef(ptr)]);

                LKBridge.FunctionInstance = null;
                LKBridge.StackCSharp = [];
            } catch (e) {
                console.error("An error occured when calling C# callback", fnc, e, label,
                    "StackCSharp:", LKBridge.StackCSharp);
            }
        });
    },

    PushObject: function (ptr) {
        LKBridge.Stack.push(LKBridge.Data.get(ptr));
    },

    CallMethod: function (ptr, str) {
        var stack = LKBridge.Stack;
        LKBridge.Stack = [];
        
        var obj = LKBridge.Data.get(ptr);
        var mName = UTF8ToString(str);
        var fnc = obj[mName];
        var result = undefined;
        
        try{
            result = fnc.apply(obj, stack);
        }catch (e) {
            console.error("Internal issue when calling " + mName + "\n", "Stack: ", stack, e)
        }
        
        return LKBridge.AddRef(LKBridge.GetOrNewRef(result));
    },

    NewInstance: function (ptr, toPtr, clazz) {
        var stack = LKBridge.Stack;
        LKBridge.Stack = [];

        var obj;
        if (ptr === 0) {
            obj = window;
        } else {
            obj = LKBridge.Data.get(ptr);
        }

        var clazz = UTF8ToString(clazz);
        
        var inst = undefined;
        try{
            inst = new (Function.prototype.bind.apply(obj[clazz], stack));
        }catch (e) {
            console.error("Internal issue when trying to instantiate " + clazz + "\n", "Stack: ", stack, e)
        }
        LKBridge.SetRef(toPtr, inst);
    },

    ShiftStack: function () {
        var v = LKBridge.StackCSharp.shift();
        return LKBridge.AddRef(LKBridge.GetOrNewRef(v));
    },

    GetFunctionInstance: function () {
        var v = LKBridge.FunctionInstance;
        return LKBridge.AddRef(LKBridge.GetOrNewRef(v));
    },

    GetString: function (ptr) {
        var value = LKBridge.Data.get(ptr);
        if (value === undefined || value === null)
            return null;

        var bufferSize = lengthBytesUTF8(value) + 1;
        var buffer = _malloc(bufferSize);
        stringToUTF8(value, buffer, bufferSize);
        return buffer;
    },

    GetNumber: function (ptr) {
        return LKBridge.Data.get(ptr);
    },

    GetBoolean: function (ptr) {
        return LKBridge.Data.get(ptr);
    },
    
    CopyData: function(ptr, buff, offset, count) {
        var value = LKBridge.Data.get(ptr);
        var arr = new Uint8Array(value, offset, count);
        HEAPU8.set(arr, buff);
    },

    RetrieveBridgeObject: function(){
        return LKBridge.AddRef(LKBridge.GetOrNewRef(LKBridge));
    },
    
    RetrieveWindowObject: function(){
        return LKBridge.AddRef(LKBridge.GetOrNewRef(window));
    },

    // Read-only WebGL remote-audio timing. The HTML audio element remains the only audible
    // output; the analyser branch is intentionally left unconnected to AudioContext.destination.
    ConvaiWebGLAudioTiming_ResumeContext: function () {
        LKBridge.EnsureAudioTimingContext();
    },

    ConvaiWebGLAudioTiming_Create: function (elementPtr) {
        var timing = LKBridge.EnsureAudioTimingContext();
        if (!timing) return 0;

        var element = LKBridge.Data.get(elementPtr);
        if (!element) return 0;

        var id = timing.nextId++;
        var now = performance.now();
        var raw = Number(element.currentTime);
        if (!isFinite(raw) || raw < 0) raw = 0;
        var probe = {
            id: id,
            element: null,
            logical: raw,
            lastRaw: raw,
            lastReadMs: now,
            state: 0,
            blocked: true,
            source: null,
            analyser: null,
            analyserBuffer: null,
            analyserStream: null,
            analyserTrack: null,
            analyserAvailable: false,
            signalActive: false,
            belowStopSinceMs: -1,
            signalStart: -1,
            signalGeneration: 0,
            discontinuityGeneration: 0,
            stallCount: 0,
            elementReplacementCount: 0,
            baseVolume: 1,
            playbackGain: 1,
            gainAnimationGeneration: 0,
            monitoring: false,
            monitorFrame: 0,
            listeners: null
        };

        timing.probes.set(id, probe);
        // NativeLib only exists while Emscripten merges this library. Call the emitted
        // runtime function instead so the generated framework does not retain a dangling
        // reference to the build-time library object.
        _ConvaiWebGLAudioTiming_SetElement(id, elementPtr);
        // Initial attachment is not a replacement/discontinuity.
        probe.discontinuityGeneration = 0;
        probe.elementReplacementCount = 0;
        // Signal state must keep advancing while no lip-sync response is active. Unity only polls
        // during an indexed response; without this monitor, the previous response can remain marked
        // active across the inter-response silence and the next response never gets a fresh onset.
        LKBridge.StartAudioTimingMonitor(probe);
        return id;
    },

    ConvaiWebGLAudioTiming_SetElement: function (probeId, elementPtr) {
        var timing = LKBridge.AudioTiming;
        var probe = timing && timing.probes.get(probeId);
        var element = LKBridge.Data.get(elementPtr);
        if (!probe || !element || probe.element === element) return;

        if (probe.element && probe.listeners) {
            probe.gainAnimationGeneration++;
            try { probe.element.volume = probe.baseVolume; } catch (error) { }
            Object.keys(probe.listeners).forEach(function (eventName) {
                probe.element.removeEventListener(eventName, probe.listeners[eventName]);
            });
            probe.elementReplacementCount++;
            probe.discontinuityGeneration++;
        }

        if (probe.source) {
            try { probe.source.disconnect(); } catch (error) { }
        }
        probe.source = null;
        probe.analyser = null;
        probe.analyserBuffer = null;
        probe.analyserStream = null;
        probe.analyserTrack = null;
        probe.analyserAvailable = false;
        probe.signalActive = false;
        probe.belowStopSinceMs = -1;
        probe.signalStart = -1;

        probe.element = element;
        var elementVolume = Number(element.volume);
        probe.baseVolume = isFinite(elementVolume) ? Math.max(0, Math.min(1, elementVolume)) : 1;
        try { element.volume = probe.baseVolume * probe.playbackGain; } catch (error) { }
        var raw = Number(element.currentTime);
        probe.lastRaw = isFinite(raw) && raw >= 0 ? raw : 0;
        probe.lastReadMs = performance.now();
        probe.blocked = !!element.paused || !!element.ended;
        probe.state = probe.blocked ? 0 : 1;

        var captureBeforeFreeze = function () {
            var eventRaw = Number(probe.element.currentTime);
            if (!isFinite(eventRaw) || eventRaw < 0) return;
            var eventDelta = eventRaw - probe.lastRaw;
            if (eventDelta < -0.001) probe.discontinuityGeneration++;
            else if (eventDelta > 0) probe.logical += eventDelta;
            probe.lastRaw = eventRaw;
            probe.lastReadMs = performance.now();
        };
        var playing = function () {
            var resumeRaw = Number(probe.element.currentTime);
            if (isFinite(resumeRaw) && resumeRaw >= 0) {
                if (resumeRaw < probe.lastRaw - 0.001 || resumeRaw > probe.lastRaw + 0.250)
                    probe.discontinuityGeneration++;
                probe.lastRaw = resumeRaw;
            }
            probe.lastReadMs = performance.now();
            probe.blocked = false;
            probe.state = 1;
        };
        var pause = function () { captureBeforeFreeze(); probe.blocked = true; probe.state = 0; };
        var ended = function () { captureBeforeFreeze(); probe.blocked = true; probe.state = 0; };
        var waiting = function () {
            captureBeforeFreeze();
            if (probe.state !== 2) probe.stallCount++;
            probe.blocked = true;
            probe.state = 2;
        };
        var stalled = function () {
            captureBeforeFreeze();
            if (probe.state !== 2) probe.stallCount++;
            probe.blocked = true;
            probe.state = 2;
        };
        probe.listeners = { playing: playing, pause: pause, ended: ended, waiting: waiting, stalled: stalled };
        Object.keys(probe.listeners).forEach(function (eventName) {
            element.addEventListener(eventName, probe.listeners[eventName]);
        });
        // Warm the analyser at attachment time. Waiting for the first lip-sync timing read can
        // miss a short first response that begins before its data-channel animation packet.
        LKBridge.EnsureAudioTimingAnalyser(probe);
    },

    ConvaiWebGLAudioTiming_Read: function (
        probeId,
        positionPtr,
        signalStartPtr,
        signalGenerationPtr,
        discontinuityGenerationPtr,
        playbackStatePtr,
        analyserAvailablePtr,
        stallCountPtr,
        elementReplacementCountPtr) {
        var timing = LKBridge.AudioTiming;
        var probe = timing && timing.probes.get(probeId);
        if (!probe || !probe.element) return 0;

        LKBridge.UpdateAudioTimingProbe(probe);

        HEAPF64[positionPtr >> 3] = probe.logical;
        HEAPF64[signalStartPtr >> 3] = probe.signalStart;
        HEAP32[signalGenerationPtr >> 2] = probe.signalGeneration;
        HEAP32[discontinuityGenerationPtr >> 2] = probe.discontinuityGeneration;
        HEAP32[playbackStatePtr >> 2] = probe.state;
        HEAP32[analyserAvailablePtr >> 2] = probe.analyserAvailable ? 1 : 0;
        HEAP32[stallCountPtr >> 2] = probe.stallCount;
        HEAP32[elementReplacementCountPtr >> 2] = probe.elementReplacementCount;
        return 1;
    },

    ConvaiWebGLAudioTiming_SetGain: function (probeId, targetGain, durationMilliseconds) {
        var timing = LKBridge.AudioTiming;
        var probe = timing && timing.probes.get(probeId);
        if (!probe || !probe.element) return;

        targetGain = Number(targetGain);
        if (!isFinite(targetGain)) targetGain = 1;
        targetGain = Math.max(0, Math.min(1, targetGain));

        durationMilliseconds = Number(durationMilliseconds);
        if (!isFinite(durationMilliseconds)) durationMilliseconds = 0;
        durationMilliseconds = Math.max(0, durationMilliseconds);

        if (probe.playbackGain >= 0.999) {
            var currentVolume = Number(probe.element.volume);
            if (isFinite(currentVolume))
                probe.baseVolume = Math.max(0, Math.min(1, currentVolume));
        }

        var generation = ++probe.gainAnimationGeneration;
        var startGain = probe.playbackGain;
        var startTime = performance.now();
        var update = function (now) {
            if (generation !== probe.gainAnimationGeneration || !probe.element) return;

            var progress = durationMilliseconds <= 0
                ? 1
                : Math.max(0, Math.min(1, (now - startTime) / durationMilliseconds));
            var remainder = 1 - progress;
            remainder *= remainder;
            probe.playbackGain = targetGain + (startGain - targetGain) * remainder;

            try {
                probe.element.volume = Math.max(
                    0,
                    Math.min(1, probe.baseVolume * probe.playbackGain));
            } catch (error) { }

            if (progress < 1)
                requestAnimationFrame(update);
        };

        if (durationMilliseconds <= 0)
            update(startTime);
        else
            requestAnimationFrame(update);
    },

    ConvaiWebGLAudioTiming_Dispose: function (probeId) {
        var timing = LKBridge.AudioTiming;
        var probe = timing && timing.probes.get(probeId);
        if (!probe) return;

        probe.monitoring = false;
        if (probe.monitorFrame) cancelAnimationFrame(probe.monitorFrame);
        probe.gainAnimationGeneration++;
        if (probe.element) {
            try { probe.element.volume = probe.baseVolume; } catch (error) { }
        }
        if (probe.element && probe.listeners) {
            Object.keys(probe.listeners).forEach(function (eventName) {
                probe.element.removeEventListener(eventName, probe.listeners[eventName]);
            });
        }
        if (probe.source) {
            try { probe.source.disconnect(); } catch (error) { }
        }
        timing.probes.delete(probeId);
    },

    // Video Receive
    NewTexture: function () {
        var tex = GLctx.createTexture();
        if (!tex){
            console.error("Failed to create a new texture for VideoReceiving")
            return LKBridge.NullPtr;
        }

        var id = GL.getNewId(GL.textures);
        tex.name = id;
        GL.textures[id] = tex;
        return id;
    },

    DestroyTexture: function (id) {
        GLctx.deleteTexture(GL.textures[id]);
    },

    AttachVideo: function (videoPtr, texId) {
        var tex = GL.textures[texId];
        var lastTime = -1;

        var initialVideo = LKBridge.Data.get(videoPtr);
        initialVideo.style.opacity = 0;
        initialVideo.style.width = 0;
        initialVideo.style.height = 0;
        setTimeout(function() {
            initialVideo.play();
        }, 0)
        initialVideo.addEventListener("canplay", (event) => {
            initialVideo.play();
        });
 
        document.body.appendChild(initialVideo);
        var updateVideo = function () {
            var video = LKBridge.Data.get(videoPtr);
            if (video === undefined) {
		        initialVideo.remove();
                return;
	        }
            
            var time = video.currentTime;
            if (!video.paused && video.srcObject !== null && time !== lastTime) {
                lastTime = time;
                GLctx.bindTexture(GLctx.TEXTURE_2D, tex);
                
                // Flip Y
                GLctx.pixelStorei(GLctx.UNPACK_FLIP_Y_WEBGL, true);
                GLctx.texImage2D(GLctx.TEXTURE_2D, 0, GLctx.RGBA, GLctx.RGBA, GLctx.UNSIGNED_BYTE, video);
                GLctx.pixelStorei(GLctx.UNPACK_FLIP_Y_WEBGL, false);

                GLctx.texParameteri(GLctx.TEXTURE_2D, GLctx.TEXTURE_MAG_FILTER, GLctx.LINEAR);
                GLctx.texParameteri(GLctx.TEXTURE_2D, GLctx.TEXTURE_MIN_FILTER, GLctx.LINEAR);
                GLctx.texParameteri(GLctx.TEXTURE_2D, GLctx.TEXTURE_WRAP_S, GLctx.CLAMP_TO_EDGE);
                GLctx.texParameteri(GLctx.TEXTURE_2D, GLctx.TEXTURE_WRAP_T, GLctx.CLAMP_TO_EDGE);
            }
            
            requestAnimationFrame(updateVideo);
        };
        
        requestAnimationFrame(updateVideo);
    },
};

autoAddDeps(NativeLib, '$LKBridge');
mergeInto(LibraryManager.library, NativeLib);
