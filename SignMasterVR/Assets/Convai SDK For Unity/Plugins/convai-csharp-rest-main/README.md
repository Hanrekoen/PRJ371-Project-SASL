# Convai REST API Client

Modern, type-safe C# client for Convai's REST APIs with Task-based async/await support and WebGL compatibility. When used through the Convai Unity SDK, follow the parent package's supported Unity-version contract.

## How the Unity SDK uses this

The Unity SDK uses this REST client internally for things like:

- API key validation in editor UI
- fetching character/narrative metadata for inspector tools
- requesting room connection details (room name + token) before joining LiveKit

If you’re integrating Convai into a Unity scene, you typically won’t call this directly — start with `Documentation~/SETUP.md`.

## Quick Start

```csharp
using Convai.RestAPI;

// Create client
var options = new ConvaiRestClientOptions("your-api-key");
using var client = new ConvaiRestClient(options);

// Get character details
var character = await client.Characters.GetDetailsAsync("character-id");

// Resolve an end user
var endUser = await client.EndUsers.GetAsync("player-42");

// Connect to a room
var roomRequest = new RoomConnectionRequest
{
    CharacterId = "character-id",
    CoreServiceUrl = "https://...",
    Transport = "livekit",
    EndUserId = "player-42"
};
var roomDetails = await client.Rooms.ConnectAsync(roomRequest);
```

## Features

- **Task-based async/await** - Modern C# async patterns with cancellation support
- **WebGL compatible** - Dual transport: `HttpClient` for Editor/Standalone, `UnityWebRequest` for WebGL
- **Type-safe** - Strongly-typed models and exceptions
- **Instance-based** - Thread-safe, disposable client with dependency injection support
- **Service organization** - Logical grouping: Characters, Users, EndUsers, Memory, Animations, Rooms, Narrative

## Installation

This SDK requires:
- a Unity version supported by the parent Convai package
- Newtonsoft.Json (via Unity Package Manager or NuGet)

## API Reference

### ConvaiRestClient

The main entry point for all API operations.

```csharp
var options = new ConvaiRestClientOptions("your-api-key")
{
    Environment = ConvaiEnvironment.Production, // or Beta
    DefaultTimeout = TimeSpan.FromSeconds(30)
};

using var client = new ConvaiRestClient(options);
```

### Character Service

```csharp
// Get character details
CharacterDetails details = await client.Characters.GetDetailsAsync("char-id");

// Enable or disable memory for a character
bool memoryEnabled = await client.Characters.GetMemoryEnabledAsync("char-id");
await client.Characters.SetMemoryEnabledAsync("char-id", enabled: true);
```

### User Service

```csharp
// Validate API key
ReferralSourceStatus status = await client.Users.ValidateApiKeyAsync();

// Update referral source
await client.Users.UpdateReferralSourceAsync("source-name");

// Get usage statistics
UserUsageData usage = await client.Users.GetUsageAsync();
```

### End User Service

```csharp
// Get one end user
EndUserDetails endUser = await client.EndUsers.GetAsync("player-42");

// List tracked end users
EndUsersListResponse endUsers = await client.EndUsers.ListAsync(limit: 100);

// Merge metadata for an end user
await client.EndUsers.UpdateMetadataAsync(
    "player-42",
    new Dictionary<string, object>
    {
        ["name"] = "Player 42",
        ["tier"] = "gold"
    });

// Delete an end user
await client.EndUsers.DeleteAsync("player-42");
```

### Memory Service

```csharp
// Add memories for a specific character + end user pair
AddMemoriesResponse added = await client.Memory.AddAsync(
    "char-id",
    "player-42",
    new[] { "The player prefers stealth missions." });

// List memories for a specific character + end user pair
MemoryListResponse memories = await client.Memory.ListAsync("char-id", "player-42");

// Delete one memory
await client.Memory.DeleteAsync("char-id", "player-42", "memory-id");

// Delete all memories for a character + end user pair
await client.Memory.DeleteAllAsync("char-id", "player-42");
```

### Animation Service

```csharp
// Get animation list
ServerAnimationListResponse list = await client.Animations.GetListAsync(page: 1, status: "active");

// Get animation details
ServerAnimationDataResponse data = await client.Animations.GetAsync("animation-id");
```

### Room Service

```csharp
var request = new RoomConnectionRequest
{
    CharacterId = "char-id",
    CoreServiceUrl = "https://live.convai.com/connect",
    Transport = "livekit",
    ConnectionType = "conversation",
    EndUserId = "player-uuid",  // Stable end-user identity for tracking, management, analytics, and memory
    EndUserMetadata = new Dictionary<string, object>
    {
        ["name"] = "Player One"
    },
    TurnDetectionConfig = TurnDetectionConfig.CreateDefault(), // Optional: explicit smart turn detection
    DefaultSttEnabled = true // Optional: initial backend STT state
};

RoomDetails room = await client.Rooms.ConnectAsync(request);
Console.WriteLine($"Connected to room: {room.RoomName}");
```

`RoomService.ConnectAsync` automatically sends `invocation_metadata` in `/connect` payloads:
- `source` defaults to `unity_sdk`
- `client_version` defaults to the current `ConvaiSDK.Version`

You can override defaults with either:
- `ConvaiRestClientOptions.InvocationSource` / `ConvaiRestClientOptions.ClientVersion`
- `RoomConnectionRequest.InvocationMetadata` values (request-level values take precedence)

### Narrative Service

```csharp
// List sections
List<SectionData> sections = await client.Narrative.ListSectionsAsync("char-id");

// Create section
CreateSectionResponse created = await client.Narrative.CreateSectionAsync(
    "char-id", "Intro", "Greet the player");

// Get section
SectionData section = await client.Narrative.GetSectionAsync("char-id", "section-id");

// Update section
EditSectionResponse updated = await client.Narrative.UpdateSectionAsync(
    "char-id", "section-id", new NarrativeSectionUpdateData { SectionName = "Welcome" });

// Delete section
await client.Narrative.DeleteSectionAsync("char-id", "section-id");

// Toggle narrative graph
await client.Narrative.ToggleNarrativeDrivenAsync("char-id", enabled: true);

// Manage decisions
await client.Narrative.AddDecisionAsync("char-id", "from-section", "to-section", "criteria");
await client.Narrative.UpdateDecisionAsync("char-id", "from", "to", "criteria", 
    new NarrativeDecisionUpdatePayload { Priority = 1 });
await client.Narrative.DeleteDecisionAsync("char-id", "from", "to", "criteria");

// Manage triggers
TriggerData trigger = await client.Narrative.CreateTriggerAsync(
    "char-id", "StartTrigger", "Player enters", destinationSection: "intro");
List<TriggerData> triggers = await client.Narrative.ListTriggersAsync("char-id");
await client.Narrative.UpdateTriggerAsync("char-id", "trigger-id", 
    new NarrativeTriggerUpdateData { TriggerName = "NewName" });
await client.Narrative.DeleteTriggerAsync("char-id", "trigger-id");
```

## Error Handling

All errors are thrown as `ConvaiRestException` with categorized error types:

```csharp
try
{
    var character = await client.Characters.GetDetailsAsync("char-id");
}
catch (ConvaiRestException ex)
{
    switch (ex.Category)
    {
        case ConvaiRestErrorCategory.Authentication:
            Console.WriteLine("Invalid API key");
            break;
        case ConvaiRestErrorCategory.NotFound:
            Console.WriteLine("Character not found");
            break;
        case ConvaiRestErrorCategory.Transport:
            Console.WriteLine($"Network error: {ex.Message}");
            break;
        case ConvaiRestErrorCategory.ParseError:
            Console.WriteLine($"Invalid response: {ex.Message}");
            break;
        default:
            Console.WriteLine($"API error: {ex.Message}");
            break;
    }
}
```

## Cancellation Support

All async methods support `CancellationToken`:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

try
{
    var character = await client.Characters.GetDetailsAsync("char-id", cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Request timed out or was cancelled");
}
```

## WebGL Considerations

The SDK automatically selects the appropriate transport:
- **Editor/Standalone/Mobile**: Uses `HttpClientTransport` (System.Net.Http)
- **WebGL Runtime**: Uses `UnityWebRequestTransport` (UnityWebRequest)

### CORS

For WebGL builds, ensure your Convai API endpoints support CORS headers. The SDK sets headers per-request to avoid WebGL preflight issues.

## Custom Transport

You can provide a custom transport implementation:

```csharp
public class CustomTransport : IConvaiHttpTransport
{
    public Task<ConvaiHttpResponse> SendAsync(ConvaiHttpRequest request, CancellationToken ct)
    {
        // Custom implementation
    }
    
    public void Dispose() { }
}

var options = new ConvaiRestClientOptions("api-key")
{
    CustomTransport = new CustomTransport()
};
```

## Project Structure

```
convai-csharp-rest-main/
├── ConvaiRestClient.cs         # Main client with service properties
├── ConvaiRestClientOptions.cs  # Configuration options
├── ConvaiRestException.cs      # Typed exceptions
├── Services/
│   ├── ConvaiServiceBase.cs    # Base class with helpers
│   ├── CharacterService.cs     # Character operations
│   ├── UserService.cs          # User operations
│   ├── EndUsersService.cs      # End-user identity operations
│   ├── MemoryService.cs        # Character-scoped memory operations
│   ├── AnimationService.cs     # Animation operations
│   ├── RoomService.cs          # Room connection
│   └── NarrativeService.cs     # Narrative design CRUD
├── Transport/
│   ├── IConvaiHttpTransport.cs # Transport interface
│   ├── ConvaiHttpRequest.cs    # Request model
│   ├── ConvaiHttpResponse.cs   # Response model
│   ├── HttpClientTransport.cs  # System.Net.Http impl
│   └── UnityWebRequestTransport.cs # Unity impl for WebGL
├── ConvaiRestModels.cs         # Request/response models
├── CharacterDetails.cs         # Character model
├── UserUsageData.cs            # Usage data model
└── Internal/                   # Shared response models
```

## Dependencies

- **Newtonsoft.Json** (v13.0.3+): JSON serialization
- **System.Net.Http**: HTTP client (Editor/Standalone)
- **UnityEngine.Networking**: UnityWebRequest (WebGL)

## License

Copyright Convai. All rights reserved.
