# Convai SDK for Unity

Build real-time conversational characters with voice and text interaction, transcripts, Actions, dynamic context, vision, emotion, lip sync, and narrative features.

## Start here

1. Read [`Documentation~/SETUP.md`](Documentation~/SETUP.md).
2. Run **GameObject → Convai → Setup Required Components**.
3. Configure credentials in **Edit → Project Settings → Convai SDK**. The generated `Assets/Resources/ConvaiSettings.asset` must remain outside version control.
4. Add `ConvaiPlayer` to the player and `ConvaiCharacter` to each conversational character.
5. Validate the scene with **GameObject → Convai → Validate Scene Setup** before entering Play Mode.

API Key mode remains the development default. Before shipping, use **Auth Token (server)** in the Credentials section to resolve short-lived credentials from your backend and keep the account API key out of the player build. See [`Documentation~/SERVER-AUTHENTICATION.md`](Documentation~/SERVER-AUTHENTICATION.md) to integrate Firebase, Auth0, PlayFab, Steam, a custom login service, or another player identity system, and [`Documentation~/PROJECT-SETTINGS.md`](Documentation~/PROJECT-SETTINGS.md#auth-mode) for the settings reference.

Use [`Documentation~/README.md`](Documentation~/README.md) for the complete documentation map, [`Documentation~/API-ENTRYPOINTS.md`](Documentation~/API-ENTRYPOINTS.md) to choose a public API, and [`Documentation~/PLATFORMS.md`](Documentation~/PLATFORMS.md) before shipping.

## Unity compatibility

Unity 6.0 and newer are supported, including the 6.0 through 6.5 release streams. The SDK selects the appropriate object-ID and object-search APIs automatically for Unity 6.0-6.3 and Unity 6.4+.

## Package and repository layouts

Published UPM artifacts expose optional samples through the `Samples~/` paths declared in `package.json`. This development repository stores them under `Samples/` for its release tooling. A release is not validated until the transformed artifact installs and imports its samples in a clean project.

Repository-maintenance scripts live at the repository root under `tools/`; they are not part of the customer package.
