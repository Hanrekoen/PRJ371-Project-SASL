# Convai Lip Sync Module

This module maps Convai lip-sync transport data to blendshape playback through profile assets, mapping assets, and a runtime playback component.

## Use this when

- you want characters to animate from Convai lip-sync data
- you need profile-specific mappings such as ARKit, MetaHuman, or CC4 Extended
- you need editor tooling for mapping, validation, or runtime inspection

## Main pieces

- `ConvaiLipSyncComponent` — runtime bridge from Convai lip-sync events to blendshape playback
- `ConvaiLipSyncProfile` — profile metadata identified by stable string IDs
- `ConvaiLipSyncMapAsset` — source-to-target mapping for a profile
- `LipSyncProfileCatalog` — runtime lookup of known profiles

## Runtime model

- profile selection is string-ID based, not enum-based
- `ConvaiLipSyncComponent` resolves its effective profile from an explicit component lock or the character-level desired profile
- mapping resolution prefers an explicit map on the component and falls back to the registered default map for that profile
- unsupported profile IDs or invalid transport payloads fail closed
- WebGL can begin lip-sync playback once browser audio is active, even if a native-style playback-start event is not emitted

## Built-in profiles

The built-in profiles are defined in code, in `Profiles/LipSyncBuiltInProfiles.cs`. They have no
assets behind them and therefore cannot fail to load. Their default blendshape maps are real content
and ship as assets inside the package:

- `SDK/Modules/LipSync/Resources/LipSync/DefaultMaps` — shipped default blendshape maps

Customers add their own profiles by dropping a `ConvaiLipSyncProfileRegistry` into any
`Resources/LipSync/ProfileRegistries/` folder; every such folder in the project is scanned and the
registries are applied in `Priority` order.

Current built-in IDs:

- `arkit`
- `metahuman`
- `cc4_extended`

## Editor surfaces

- `SDK/Editor/LipSync/ConvaiLipSyncComponentEditor.cs` — `ConvaiLipSyncComponentEditor`
- `SDK/Editor/LipSync/ConvaiLipSyncMapAssetEditor.cs` — `ConvaiLipSyncMapAssetEditor`
- `SDK/Editor/LipSync/ConvaiLipSyncMapDebugWindow.cs` — `ConvaiLipSyncMapDebugWindow`
- `SDK/Editor/LipSync/ConvaiLipSyncProfileEditor.cs` — `ConvaiLipSyncProfileEditor`

These editors validate unknown profile IDs, duplicate catalog entries, missing default maps, and invalid profile transport configuration.

## Go deeper (paths from package root)

- Runtime component: `SDK/Modules/LipSync/Components/ConvaiLipSyncComponent.cs`
- Mapping asset: `SDK/Modules/LipSync/Mapping/ConvaiLipSyncMapAsset.cs`
- Default map registry: `SDK/Modules/LipSync/Mapping/ConvaiLipSyncDefaultMapRegistry.cs`
- Profile asset: `SDK/Modules/LipSync/Profiles/ConvaiLipSyncProfile.cs`
- Profile catalog: `SDK/Modules/LipSync/Profiles/LipSyncProfileCatalog.cs`
- In-repo overview: `Documentation~/SOURCE-REFERENCE.md`
