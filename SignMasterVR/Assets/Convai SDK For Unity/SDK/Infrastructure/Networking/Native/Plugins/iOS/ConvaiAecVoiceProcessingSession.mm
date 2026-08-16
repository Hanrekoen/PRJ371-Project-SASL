#import <AVFoundation/AVFoundation.h>

static NSString *g_prevCategory = nil;
static NSString *g_prevMode = nil;
static AVAudioSessionCategoryOptions g_prevOptions = 0;
static BOOL g_sessionConfigured = NO;

static BOOL Convai_AecRouteUsesExternalOutput(AVAudioSessionRouteDescription *route)
{
    if (route == nil)
        return NO;

    for (AVAudioSessionPortDescription *port in route.outputs)
    {
        NSString *portType = port.portType;
        if ([portType isEqualToString:AVAudioSessionPortHeadphones] ||
            [portType isEqualToString:AVAudioSessionPortHeadsetMic] ||
            [portType isEqualToString:AVAudioSessionPortLineOut] ||
            [portType isEqualToString:AVAudioSessionPortBluetoothA2DP] ||
            [portType isEqualToString:AVAudioSessionPortBluetoothHFP] ||
            [portType isEqualToString:AVAudioSessionPortBluetoothLE] ||
            [portType isEqualToString:AVAudioSessionPortUSBAudio] ||
            [portType isEqualToString:AVAudioSessionPortAirPlay])
            return YES;
    }

    return NO;
}
static int Convai_AecRouteBitForPort(AVAudioSessionPortDescription *port)
{
    if (port == nil)
        return 0;

    NSString *portType = port.portType;
    if ([portType isEqualToString:AVAudioSessionPortBuiltInSpeaker])
        return 1;
    if ([portType isEqualToString:AVAudioSessionPortBuiltInReceiver])
        return 2;
    if ([portType isEqualToString:AVAudioSessionPortHeadphones] ||
        [portType isEqualToString:AVAudioSessionPortHeadsetMic] ||
        [portType isEqualToString:AVAudioSessionPortLineOut])
        return 4;
    if ([portType isEqualToString:AVAudioSessionPortBluetoothA2DP] ||
        [portType isEqualToString:AVAudioSessionPortBluetoothHFP] ||
        [portType isEqualToString:AVAudioSessionPortBluetoothLE])
        return 8;
    if ([portType isEqualToString:AVAudioSessionPortUSBAudio])
        return 16;
    if ([portType isEqualToString:AVAudioSessionPortAirPlay])
        return 32;

    return 64;
}

extern "C" bool Convai_AecVoiceProcessingSession_Begin(void)
{
    @autoreleasepool
    {
        AVAudioSession *session = [AVAudioSession sharedInstance];
        NSError *error = nil;

        if (!g_sessionConfigured)
        {
            g_prevCategory = session.category;
            g_prevMode = session.mode;
            g_prevOptions = session.categoryOptions;
        }

        AVAudioSessionCategoryOptions options =
            AVAudioSessionCategoryOptionDefaultToSpeaker |
            AVAudioSessionCategoryOptionAllowBluetooth |
            AVAudioSessionCategoryOptionAllowBluetoothA2DP;

        // Default mode can yield louder speaker playback than call-oriented modes while still
        // allowing duplex audio through PlayAndRecord. Keep speaker routing explicit below.
        BOOL ok = [session setCategory:AVAudioSessionCategoryPlayAndRecord
                                  mode:AVAudioSessionModeDefault
                               options:options
                                 error:&error];
        if (!ok)
            return false;

        ok = [session setActive:YES error:&error];
        if (!ok)
            return false;

        if (!Convai_AecRouteUsesExternalOutput(session.currentRoute))
        {
            ok = [session overrideOutputAudioPort:AVAudioSessionPortOverrideSpeaker error:&error];
            if (!ok)
                return false;
        }
        g_sessionConfigured = YES;
        return true;
    }
}

extern "C" void Convai_AecVoiceProcessingSession_End(void)
{
    @autoreleasepool
    {
        if (!g_sessionConfigured)
            return;

        AVAudioSession *session = [AVAudioSession sharedInstance];
        NSError *error = nil;

        [session overrideOutputAudioPort:AVAudioSessionPortOverrideNone error:&error];
        NSString *category = g_prevCategory ?: AVAudioSessionCategorySoloAmbient;
        NSString *mode = g_prevMode ?: AVAudioSessionModeDefault;

        [session setCategory:category mode:mode options:g_prevOptions error:&error];

        g_sessionConfigured = NO;
    }
}

extern "C" int Convai_AecVoiceProcessingSession_GetRouteSignature(void)
{
    @autoreleasepool
    {
        AVAudioSessionRouteDescription *route = [AVAudioSession sharedInstance].currentRoute;
        int outputBits = 0;
        for (AVAudioSessionPortDescription *port in route.outputs)
            outputBits |= Convai_AecRouteBitForPort(port);

        int inputBits = 0;
        for (AVAudioSessionPortDescription *port in route.inputs)
            inputBits |= Convai_AecRouteBitForPort(port);

        return outputBits | (inputBits << 16);
    }
}
