using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using SignMasterVR.Lessons;
using SignMasterVR.Tutor;
using SignMasterVR.UI;
using SignMasterVR.Core;
using SignMasterVR.ML;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace SignMasterVR.EditorTools
{
    /// <summary>
    /// One-click setup tools that build the Week-1 demo for you: 26 letter
    /// GestureData assets + Level 1, a placeholder tutor (primitives + a
    /// simple Animator, no external model needed), the Lesson UI canvas, and
    /// the full wiring between them in whatever scene is currently open.
    ///
    /// Run them in order from the Unity menu bar:
    ///   Tools > SignMasterVR > 0 Build Classroom Environment
    ///   Tools > SignMasterVR > 1 Generate Alphabet Level (A-Z)
    ///   Tools > SignMasterVR > 2 Build Placeholder Tutor Prefab
    ///   Tools > SignMasterVR > 3 Build Lesson UI Prefab
    ///   Tools > SignMasterVR > 4 Wire Up Current Scene
    ///   Tools > SignMasterVR > 5 Improve Player Rig (stationary, positioned)
    ///   Tools > SignMasterVR > 6 Build Main Menu Scene
    ///   Tools > SignMasterVR > 7 Fix Tutor Avatar (materials + rig + ground)
    ///     — run this once, any time after you've dragged a real avatar (e.g.
    ///     Mixamo, named "character") into Tutor.prefab in place of the
    ///     primitives. Not part of RUN ALL since it depends on that avatar
    ///     already being there.
    ///   Tools > SignMasterVR > 7b Measure Tutor Ground Offset (read-only)
    ///     — if the avatar is still floating/sunk after Step 7, run this
    ///     (ClassRoom.unity open, Tutor visible) and send Claude the number
    ///     it logs.
    ///   Tools > SignMasterVR > 8 Assign Character Textures
    ///     — run this once you've dropped Ch07_body_diffuse.png / _normal.png /
    ///     _mask.png and Ch07_hair_diffuse.png / _normal.png into
    ///     Assets/Texture_and_materials/Character/. Wires them into the
    ///     Ch07_body / Ch07_hair materials so the avatar stops rendering flat
    ///     white. Not part of RUN ALL since it depends on those PNGs existing.
    ///   Tools > SignMasterVR > 9 Assign Alphabet Reference Images
    ///     — run this once Step 1 has created the 26 Gesture_X assets and
    ///     Letter_A.png..Letter_Z.png exist in
    ///     Assets/Texture_and_materials/Alphabet/. Imports each as a Sprite
    ///     and assigns it to that letter's GestureData.referenceImage, so the
    ///     Lesson UI's reference-image panel actually shows the handshape.
    ///     Not part of RUN ALL since it depends on those PNGs existing.
    ///   Tools > SignMasterVR > 10 Assign Real Idle Animation
    ///     -- run this once you've dropped a Mixamo Idle animation (FBX for
    ///     Unity, Without Skin, exported from the SAME rig session as
    ///     character.fbx so bone names still start with mixamorig8:) into
    ///     Assets/Animations/Tutor/Idle.fbx. Replaces the placeholder
    ///     head-wiggle Idle state's motion with the real full-body clip, so
    ///     the tutor settles into an actual rest pose instead of the T-pose.
    ///     Not part of RUN ALL since it depends on that FBX existing.
    ///   Tools > SignMasterVR > 11 Assign Feedback Audio
    ///     -- run this once you've dropped Correct.wav, TryAgain.wav and
    ///     LevelComplete.wav (mp3/ogg also fine) into Assets/Audio/Feedback/.
    ///     Wires them into the AudioManager on "LessonSystem" under the keys
    ///     every GestureData/LessonManager call already expects, so the three
    ///     feedback stings just start playing -- no other wiring needed.
    ///     Not part of RUN ALL since it depends on ClassRoom already being
    ///     wired (Step 4) and those audio files existing.
    ///   Tools > SignMasterVR > 12 Apply Neon Menu Style
    ///     -- restyles the ALREADY-BUILT Main Menu in place: neon cyan text,
    ///     near-black background, Reset Progress in neon pink. Safe to
    ///     re-run any time -- only touches colors on existing objects, never
    ///     rebuilds/duplicates them. Not part of RUN ALL since it depends on
    ///     Step 6 already having built MainMenu.unity. (The Lesson canvas's
    ///     bigger size/fonts/cyan text needs no equivalent step -- just
    ///     re-run Step 3, which rebuilds that whole prefab from scratch.)
    ///   Tools > SignMasterVR > 13 Switch To Network Gesture Recognizer
    ///     -- points LessonManager at NetworkGestureRecognizer instead of
    ///     FakeGestureRecognizer, so gesture results come from an external
    ///     webcam on a laptop (GestureServer/server.py, at the repo root
    ///     next to Assets/) instead of the keyboard/debug buttons. Set the
    ///     laptop's IP/port on the NetworkGestureRecognizer component in the
    ///     Inspector afterward. Not part of RUN ALL since it depends on
    ///     Step 4 already having run.
    ///   Tools > SignMasterVR > 14 Register Level 2 (Phrases)
    ///     -- the ML team's make_unity_assets.py already generated
    ///     Level_2_Phrases.asset (16 GestureData assets: Thank you, Please,
    ///     Sorry, Help, Home, Toilet, Drive, I Sign, I am deaf, Can you sign?,
    ///     Nice to meet you, How are you, I am, My pleasure, Hello, Bye) but
    ///     never added it to LevelManager.allLevels, so it existed on disk but
    ///     was unreachable in Play mode. This adds it after Level 1 and sets
    ///     its required-previous-level so it only unlocks once Level 1 is
    ///     complete. Not part of RUN ALL since it depends on Step 4 already
    ///     having run.
    ///   Tools > SignMasterVR > 15 Add Ghost Hand Demo
    ///     -- wires the ML team's GhostHandPlayer.cs into the scene: adds it
    ///     as a child of the Tutor, points LessonManager.ghostHand at it, and
    ///     creates Assets/StreamingAssets/GhostHands/ if it isn't there yet.
    ///     It plays automatically whenever a gesture loads or a learner gets
    ///     one wrong -- but only once clip JSON files actually exist in that
    ///     folder (run `python export_ghost_hand.py --out ../Assets/StreamingAssets/GhostHands`
    ///     in GestureServer/ first). No clips yet = the ghost silently stays
    ///     hidden, nothing breaks. Not part of RUN ALL since it depends on
    ///     Step 4 already having run.
    ///   Tools > SignMasterVR > 16 Assign Prompt Voiceovers
    ///     -- run this once the 41 prompt mp3s are in Assets/Audio/Prompts/.
    ///     Fills in GestureData.promptAudioKey on every gesture asset AND
    ///     registers the clips on the AudioManager living on "LessonSystem"
    ///     in ClassRoom, then saves the scene itself -- the scene half is
    ///     easy to lose if you forget Ctrl+S, which is exactly what happened
    ///     the first time (41 asset keys, 0 scene entries).
    ///   Tools > SignMasterVR > 17 Make Scenes VR-Ready
    ///     -- RUN THIS BEFORE EVERY BUILD YOU PUT ON A HEADSET. It fixes the
    ///     three things that made the app render pure black on a Quest while
    ///     looking perfect in the Editor: a Main Menu with no XR Origin (so
    ///     no head pose), a ScreenSpaceOverlay canvas (nothing in the world
    ///     to draw on), and StandaloneInputModule on the EventSystem of both
    ///     scenes (throws InvalidOperationException every frame, because this
    ///     project uses the Input System package only). It also attaches a
    ///     WorldPanelPlacer to the menu canvas so the panel appears in front
    ///     of wherever the learner is actually looking -- a fixed world
    ///     position assumes a tracking origin mode and a startup facing
    ///     direction we do not control, and the first attempt put the menu on
    ///     the floor at the learner's feet. It adds an AudioListener only if
    ///     the scene genuinely has none; the XR rig prefab already brings one,
    ///     so normally this is a no-op.
    ///     Idempotent -- safe to run as often as you like. Step 6 now calls
    ///     it inline, so a freshly built menu is VR-correct from the start.
    /// ...or just run "RUN ALL (0-6)" once everything below has compiled cleanly.
    ///
    /// Safe to re-run any step — each one looks for what it already built
    /// instead of duplicating it.
    /// </summary>
    public static class SignMasterVREditorSetup
    {
        private const string GestureFolder = "Assets/Data/Gestures";
        private const string LevelFolder = "Assets/Data/Levels";
        private const string PrefabFolder = "Assets/Prefabs";
        private const string AnimFolder = "Assets/Animations/Tutor";
        private const string Level1Path = LevelFolder + "/Level_1_Letters.asset";
        private const string Level2Path = LevelFolder + "/Level_2_Phrases.asset";
        private const string TutorPrefabPath = PrefabFolder + "/Tutor.prefab";
        private const string CanvasPrefabPath = PrefabFolder + "/LessonCanvas.prefab";
        private const string GhostHandsFolder = "Assets/StreamingAssets/GhostHands";
        private const string AnimatorControllerPath = AnimFolder + "/TutorAnimator.controller";
        private const string CharacterTextureFolder = "Assets/Texture_and_materials/Character";
        private const string AlphabetImageFolder = "Assets/Texture_and_materials/Alphabet";
        private const string FeedbackAudioFolder = "Assets/Audio/Feedback";
        private const string PromptAudioFolder = "Assets/Audio/Prompts";

        // Shared neon UI palette (Lesson canvas + Main Menu cosmetic pass).
        private static readonly Color NeonCyan = new Color(0.05f, 0.95f, 1f);
        private static readonly Color NeonPink = new Color(1f, 0.1f, 0.65f);
        private static readonly Color MenuNearBlack = new Color(0.02f, 0.02f, 0.04f);
        // Warm, but not green: Skip must never read as "you got it right".
        private static readonly Color SkipAmber = new Color(1f, 0.78f, 0.25f);
        private static readonly Color MenuPanelDark = new Color(0.06f, 0.08f, 0.1f, 0.95f);

        // ------------------------------------------------------------------
        // STEP 0 — Classroom environment (floor, walls, board, desk, shelves,
        // a standing mark for the learner, and a light if the scene has none)
        // ------------------------------------------------------------------
        [MenuItem("Tools/SignMasterVR/0 Build Classroom Environment")]
        public static void BuildClassroomEnvironment()
        {
            if (!RequireActiveSceneIsNotMainMenu()) return;

            Material ground = LoadOrFallback("Assets/Room/Materials/Ground.mat", new Color(0.55f, 0.55f, 0.6f));
            Material wallMat = LoadOrFallback("Assets/Room/Materials/Walls.mat", new Color(0.85f, 0.85f, 0.82f));
            Material tableTop = LoadOrFallback("Assets/Room/Materials/TableTop.mat", new Color(0.4f, 0.25f, 0.15f));
            Material tableLegs = LoadOrFallback("Assets/Room/Materials/TableLegs.mat", new Color(0.2f, 0.2f, 0.2f));

            GameObject env = GameObject.Find("Environment");
            if (env == null) env = new GameObject("Environment");

            // Floor/walls reuse Ground.mat / Walls.mat from Assets/Room/Materials, which already
            // existed in the project before I touched anything — if either material is already
            // used by something in the open scene, we assume a floor/walls already exist there
            // and skip creating a duplicate room on top of it.
            if (!MaterialAlreadyUsedInScene(ground))
            {
                // Unity's built-in Plane primitive is exactly 10x10m at scale 1 — matches the
                // original plan's own "10m x 10m is plenty" sizing, no manual scaling needed.
                CreatePart(env.transform, "Floor", PrimitiveType.Plane, Vector3.zero, Vector3.one, ground, false);
            }
            else
            {
                Debug.Log("[SignMasterVR] Ground material is already used elsewhere in this scene — skipping a duplicate floor.");
            }

            if (!MaterialAlreadyUsedInScene(wallMat))
            {
                // Back wall sits beyond the tutor (z=+5); left/right walls run the depth of the
                // room; the front (player's side, z=-5) is deliberately left open per the plan.
                CreatePart(env.transform, "BackWall", PrimitiveType.Cube, new Vector3(0, 1.5f, 5f), new Vector3(10f, 3f, 0.2f), wallMat, false);
                CreatePart(env.transform, "LeftWall", PrimitiveType.Cube, new Vector3(-5f, 1.5f, 0f), new Vector3(0.2f, 3f, 10f), wallMat, false);
                CreatePart(env.transform, "RightWall", PrimitiveType.Cube, new Vector3(5f, 1.5f, 0f), new Vector3(0.2f, 3f, 10f), wallMat, false);
            }
            else
            {
                Debug.Log("[SignMasterVR] Walls material is already used elsewhere in this scene — skipping duplicate walls.");
            }

            // Learning board + branding on the back wall — kept high and off-centre-stage so it
            // never competes with the tutor's hands, which is the one thing the plan is emphatic
            // the learner's attention should stay on.
            CreatePart(env.transform, "LearningBoard", PrimitiveType.Cube, new Vector3(0, 1.7f, 4.85f), new Vector3(3f, 1.6f, 0.05f), LoadOrFallback(null, new Color(0.08f, 0.3f, 0.15f)), false);
            CreateWorldText(env.transform, "BrandingText", "SignMaster VR", new Vector3(0, 2.75f, 4.83f), 3f, Color.white);

            // Teacher desk, off to the side (not centred in front of the tutor) so it never
            // blocks the learner's view of the demonstration.
            if (!MaterialAlreadyUsedInScene(tableTop))
            {
                Transform desk = new GameObject("TeacherDesk").transform;
                desk.SetParent(env.transform, false);
                desk.localPosition = new Vector3(-2.6f, 0f, 3.2f);
                CreatePart(desk, "DeskTop", PrimitiveType.Cube, new Vector3(0, 0.75f, 0), new Vector3(1.2f, 0.05f, 0.6f), tableTop, false);
                CreatePart(desk, "LegA", PrimitiveType.Cube, new Vector3(-0.5f, 0.375f, -0.25f), new Vector3(0.05f, 0.75f, 0.05f), tableLegs, false);
                CreatePart(desk, "LegB", PrimitiveType.Cube, new Vector3(0.5f, 0.375f, -0.25f), new Vector3(0.05f, 0.75f, 0.05f), tableLegs, false);
                CreatePart(desk, "LegC", PrimitiveType.Cube, new Vector3(-0.5f, 0.375f, 0.25f), new Vector3(0.05f, 0.75f, 0.05f), tableLegs, false);
                CreatePart(desk, "LegD", PrimitiveType.Cube, new Vector3(0.5f, 0.375f, 0.25f), new Vector3(0.05f, 0.75f, 0.05f), tableLegs, false);
            }
            else
            {
                Debug.Log("[SignMasterVR] TableTop material is already used elsewhere in this scene — assuming a desk/table already exists, skipping a duplicate.");
            }

            // A couple of simple shelves against the right wall, well away from the tutor's
            // sightline, using their own neutral material rather than the desk's so this doesn't
            // get skipped by the desk's duplicate-check above.
            Material shelfMat = LoadOrFallback(null, new Color(0.5f, 0.35f, 0.22f));
            Transform shelf = new GameObject("Shelves").transform;
            shelf.SetParent(env.transform, false);
            shelf.localPosition = new Vector3(4.3f, 0, -3f);
            CreatePart(shelf, "ShelfLow", PrimitiveType.Cube, new Vector3(0, 0.9f, 0), new Vector3(0.4f, 0.05f, 1.4f), shelfMat, false);
            CreatePart(shelf, "ShelfHigh", PrimitiveType.Cube, new Vector3(0, 1.6f, 0), new Vector3(0.4f, 0.05f, 1.4f), shelfMat, false);
            CreatePart(shelf, "ShelfBack", PrimitiveType.Cube, new Vector3(0, 1.2f, 0.65f), new Vector3(0.4f, 1.6f, 0.05f), shelfMat, false);

            // A flat mark on the floor showing the learner where to stand for the stationary
            // setup (Phase 3 — no locomotion). Kept in front of the tutor, clear of the desk/shelves.
            CreatePart(env.transform, "LearnerStandingMark", PrimitiveType.Cylinder, new Vector3(0, 0.005f, -2f), new Vector3(0.6f, 0.005f, 0.6f), LoadOrFallback(null, new Color(0.2f, 0.6f, 0.9f)), true);

            // One directional light, only if the scene doesn't already have one — the plan is
            // explicit about not piling on real-time lights for Quest performance.
            bool hasDirectional = false;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) hasDirectional = true;
            if (!hasDirectional)
            {
                GameObject lightGO = new GameObject("Sun (Directional Light)");
                var light = lightGO.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                light.shadows = LightShadows.Soft;
                lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            EditorUtility.SetDirty(env);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[SignMasterVR] Classroom environment built under an \"Environment\" GameObject. Branding text size is a rough guess since I can't preview it in-Editor — resize the BrandingText object's Font Size or scale if it looks off.");
        }

        [MenuItem("Tools/SignMasterVR/Bake Lighting (optional, can take a while)")]
        public static void BakeLighting()
        {
            Lightmapping.Bake();
        }

        // ------------------------------------------------------------------
        // STEP 1 — Data: all 26 letters + Level 1
        // ------------------------------------------------------------------
        [MenuItem("Tools/SignMasterVR/1 Generate Alphabet Level (A-Z)")]
        public static void GenerateAlphabetLevel()
        {
            EnsureFolder(GestureFolder);
            EnsureFolder(LevelFolder);

            var gestureAssets = new List<GestureData>();
            for (char c = 'A'; c <= 'Z'; c++)
            {
                string letter = c.ToString();
                string path = $"{GestureFolder}/Gesture_{letter}.asset";
                GestureData g = AssetDatabase.LoadAssetAtPath<GestureData>(path);
                if (g == null)
                {
                    g = ScriptableObject.CreateInstance<GestureData>();
                    AssetDatabase.CreateAsset(g, path);
                }
                g.gestureId = letter;
                g.displayName = $"Letter {letter}";
                g.tutorAnimationTrigger = "Active"; // shared placeholder cue — see TutorController
                g.requiredConfidence = 0.75f;
                g.successMessage = "Excellent!";
                g.successAudioKey = "Correct";
                g.tryAgainAudioKey = "TryAgain";
                EditorUtility.SetDirty(g);
                gestureAssets.Add(g);
            }

            LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(Level1Path);
            if (level == null)
            {
                level = ScriptableObject.CreateInstance<LevelData>();
                AssetDatabase.CreateAsset(level, Level1Path);
            }
            level.levelNumber = 1;
            level.levelName = "Letters";
            level.description = "Learn the 26 letters of the SASL manual alphabet.";
            level.gestures = gestureAssets.ToArray();
            level.requiredPreviousLevel = null;
            EditorUtility.SetDirty(level);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SignMasterVR] Generated {gestureAssets.Count} letter GestureData assets and Level 1 (Letters) at {Level1Path}.");
        }

        // ------------------------------------------------------------------
        // STEP 2 — Placeholder tutor (primitives, no external model required)
        // ------------------------------------------------------------------
        [MenuItem("Tools/SignMasterVR/2 Build Placeholder Tutor Prefab")]
        public static void BuildTutorPrefab()
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Animations");
            EnsureFolder(AnimFolder);

            AnimationClip idleClip = CreateRotationClip(
                "TutorIdleClip", "Head", "localEulerAnglesRaw.x",
                new Keyframe(0, 0), new Keyframe(1f, 4f), new Keyframe(2f, 0f));
            SetLooping(idleClip, true);
            SaveClipAsset(idleClip, $"{AnimFolder}/TutorIdleClip.anim");

            AnimationClip activeClip = CreateRotationClip(
                "TutorActiveClip", "Head", "localEulerAnglesRaw.x",
                new Keyframe(0, 0), new Keyframe(0.2f, -12f), new Keyframe(0.45f, 10f), new Keyframe(0.7f, -6f), new Keyframe(1f, 0f));
            SetLooping(activeClip, false);
            SaveClipAsset(activeClip, $"{AnimFolder}/TutorActiveClip.anim");

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(AnimatorControllerPath);

            if (!ParamExists(controller, "Active"))
                controller.AddParameter("Active", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine rootSM = controller.layers[0].stateMachine;
            AnimatorState idleState = FindOrAddState(rootSM, "Idle", idleClip);
            AnimatorState activeState = FindOrAddState(rootSM, "Active", activeClip);
            rootSM.defaultState = idleState;

            EnsureAnyStateTransition(rootSM, activeState, "Active");
            EnsureExitTransition(activeState, idleState);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            // Build a purely primitive-based body so no external avatar is needed this week.
            GameObject root = new GameObject("Tutor_BUILD_TEMP");
            Transform body = FindOrCreateChild(root.transform, "Body", PrimitiveType.Capsule,
                new Vector3(0, 1f, 0), new Vector3(0.6f, 0.6f, 0.6f), new Color(0.2f, 0.4f, 0.8f));
            Transform head = FindOrCreateChild(root.transform, "Head", PrimitiveType.Cube,
                new Vector3(0, 1.85f, 0), new Vector3(0.35f, 0.35f, 0.35f), new Color(0.9f, 0.8f, 0.7f));
            Transform light = FindOrCreateChild(root.transform, "StateLight", PrimitiveType.Sphere,
                new Vector3(0, 2.35f, 0), new Vector3(0.15f, 0.15f, 0.15f), Color.white);

            foreach (var col in root.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(col);

            Animator animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            AudioSource audioSource = root.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;

            TutorController tc = root.AddComponent<TutorController>();
            tc.animator = animator;
            tc.voiceSource = audioSource;
            tc.stateLight = light.GetComponent<Renderer>();

            PrefabUtility.SaveAsPrefabAsset(root, TutorPrefabPath);
            Object.DestroyImmediate(root);

            AssetDatabase.SaveAssets();
            Debug.Log($"[SignMasterVR] Placeholder Tutor prefab built at {TutorPrefabPath} (capsule body, cube head, colored state light — swap for a real avatar later).");
        }

        // ------------------------------------------------------------------
        // STEP 3 — Lesson UI (World Space canvas)
        // ------------------------------------------------------------------
        [MenuItem("Tools/SignMasterVR/3 Build Lesson UI Prefab")]
        public static void BuildLessonUI()
        {
            EnsureFolder(PrefabFolder);

            GameObject canvasGO = new GameObject("LessonCanvas_BUILD_TEMP", typeof(RectTransform));
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            RectTransform canvasRect = canvasGO.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(1000, 650);
            canvasGO.transform.localScale = Vector3.one * 0.0023f; // ~2.3m wide panel in world space

            // Layout deliberately spread out with generous gaps between elements -- the first pass
            // packed everything too tight once fonts got bigger (title crowded the letter, buttons
            // crowded each other and the bottom edge).
            TMP_Text levelTitle = CreateText(canvasRect, "LevelTitleText", "LEVEL 1\nLETTERS", 40,
                TextAlignmentOptions.TopLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -30), new Vector2(420, 100), NeonCyan);
            TMP_Text gestureName = CreateText(canvasRect, "GestureNameText", "A", 68,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 130), new Vector2(700, 120), NeonCyan);
            TMP_Text progressText = CreateText(canvasRect, "ProgressText", "1 / 26", 30,
                TextAlignmentOptions.TopRight, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -30), new Vector2(220, 50), NeonCyan);

            GameObject imageGO = CreateImage(canvasRect, "ReferenceImage",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -75), new Vector2(260, 260));
            Image referenceImage = imageGO.GetComponent<Image>();
            referenceImage.enabled = false;

            GameObject feedbackPanel = CreatePanel(canvasRect, "FeedbackPanel", new Color(0, 0, 0, 0.6f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 115), new Vector2(620, 170));
            TMP_Text feedbackText = CreateText(feedbackPanel.GetComponent<RectTransform>(), "FeedbackText", "", 44,
                TextAlignmentOptions.Center, new Vector2(0, 0.4f), new Vector2(1, 1), Vector2.zero, Vector2.zero, NeonCyan);
            TMP_Text feedbackConfidence = CreateText(feedbackPanel.GetComponent<RectTransform>(), "FeedbackConfidenceText", "", 28,
                TextAlignmentOptions.Center, new Vector2(0, 0f), new Vector2(1, 0.4f), Vector2.zero, Vector2.zero, NeonCyan);
            feedbackPanel.SetActive(false);

            GameObject levelCompletePanel = CreatePanel(canvasRect, "LevelCompletePanel", new Color(0, 0, 0, 0.85f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(740, 370));
            TMP_Text levelCompleteText = CreateText(levelCompletePanel.GetComponent<RectTransform>(), "LevelCompleteText", "", 42,
                TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, NeonCyan);
            levelCompletePanel.SetActive(false);

            // Debug test buttons (Phase 15 - Fake ML). Wired to FakeGestureRecognizer in Step 4.
            // Left as semantic green/red (not part of the neon cosmetic pass) so pass/fail stays obvious.
            // NOTE: CreateButton's RectTransform keeps Unity's default pivot (0.5, 0.5), so anchoredPos.x
            // below is each button's CENTER, not its left edge, measured from the canvas's left edge (anchor 0,0).
            // Centered as a pair on the 1000-wide canvas: pair spans x=260..740 (center x=500), 40px gap between them.
            CreateButton(canvasRect, "TestCorrectButton", "TEST CORRECT",
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(370, 55), new Vector2(220, 54), new Color(0.2f, 0.6f, 0.2f), Color.white);
            CreateButton(canvasRect, "TestWrongButton", "TEST WRONG",
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(630, 55), new Vector2(220, 54), new Color(0.6f, 0.2f, 0.2f), Color.white);

            UIManager ui = canvasGO.AddComponent<UIManager>();
            ui.levelTitleText = levelTitle;
            ui.gestureNameText = gestureName;
            ui.progressText = progressText;
            ui.referenceImage = referenceImage;
            ui.feedbackPanel = feedbackPanel;
            ui.feedbackText = feedbackText;
            ui.feedbackConfidenceText = feedbackConfidence;
            ui.levelCompletePanel = levelCompletePanel;
            ui.levelCompleteText = levelCompleteText;

            PrefabUtility.SaveAsPrefabAsset(canvasGO, CanvasPrefabPath);
            Object.DestroyImmediate(canvasGO);

            AssetDatabase.SaveAssets();
            Debug.Log($"[SignMasterVR] Lesson UI prefab built at {CanvasPrefabPath}.");
        }

        // ------------------------------------------------------------------
        // STEP 4 — Wire everything into the currently open scene
        // ------------------------------------------------------------------
        [MenuItem("Tools/SignMasterVR/4 Wire Up Current Scene")]
        public static void WireUpScene()
        {
            if (!RequireActiveSceneIsNotMainMenu()) return;

            LevelData level1 = AssetDatabase.LoadAssetAtPath<LevelData>(Level1Path);
            GameObject tutorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TutorPrefabPath);
            GameObject canvasPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CanvasPrefabPath);

            if (level1 == null || tutorPrefab == null || canvasPrefab == null)
            {
                Debug.LogError("[SignMasterVR] Run steps 1-3 first — missing Level 1 data, Tutor prefab, or LessonCanvas prefab.");
                return;
            }

            GameObject tutorInstance = GameObject.Find("Tutor");
            if (tutorInstance == null)
            {
                tutorInstance = (GameObject)PrefabUtility.InstantiatePrefab(tutorPrefab);
                tutorInstance.name = "Tutor";
                tutorInstance.transform.position = new Vector3(0, 0, 2.5f);
                tutorInstance.transform.rotation = Quaternion.Euler(0, 180, 0);
            }

            GameObject canvasInstance = GameObject.Find("LessonCanvas");
            if (canvasInstance == null)
            {
                canvasInstance = (GameObject)PrefabUtility.InstantiatePrefab(canvasPrefab);
                canvasInstance.name = "LessonCanvas";
                // Mounted on the wall just in front of LearningBoard (Step 0: centered at
                // x=0, y=1.7, z=4.85, 3m x 1.6m) rather than floating close to the player --
                // sits ~0.15m in front of its face, raised above the board's own vertical
                // center for a comfortable eye-level read (a bit of overhang past the top
                // of the board is fine -- it's a floating panel, not physically mounted).
                canvasInstance.transform.position = new Vector3(0, 2f, 4.7f);
            }
            Canvas canvas = canvasInstance.GetComponent<Canvas>();
            if (canvas != null)
            {
                // Look for the actual camera under the Player rig first — Camera.main depends on
                // the camera being tagged "MainCamera", which isn't guaranteed on every XR
                // template version. Falls back to Camera.main if no Player rig is in the scene.
                GameObject playerRig = GameObject.Find("Player");
                Camera cam = playerRig != null ? playerRig.GetComponentInChildren<Camera>(true) : null;
                canvas.worldCamera = cam != null ? cam : Camera.main;
            }

            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                GameObject es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                // XRUIInputModule, NOT StandaloneInputModule. This is where ClassRoom's broken
                // EventSystem came from: the legacy module reads UnityEngine.Input, which this
                // project disables (Active Input Handling = Input System Package), so it threw
                // InvalidOperationException on every frame of the build -- thousands of
                // exceptions a second, and a UI that could never be clicked.
                es.AddComponent<XRUIInputModule>();
            }

            GameObject managerGO = GameObject.Find("LessonSystem");
            if (managerGO == null) managerGO = new GameObject("LessonSystem");

            var progress = managerGO.GetComponent<ProgressManager>() ?? managerGO.AddComponent<ProgressManager>();
            var audio = managerGO.GetComponent<AudioManager>() ?? managerGO.AddComponent<AudioManager>();
            if (audio.source == null)
                audio.source = managerGO.GetComponent<AudioSource>() ?? managerGO.AddComponent<AudioSource>();

            var levelManager = managerGO.GetComponent<LevelManager>() ?? managerGO.AddComponent<LevelManager>();
            levelManager.allLevels = new LevelData[] { level1 };

            var recognizer = managerGO.GetComponent<FakeGestureRecognizer>() ?? managerGO.AddComponent<FakeGestureRecognizer>();

            var lessonManager = managerGO.GetComponent<LessonManager>() ?? managerGO.AddComponent<LessonManager>();
            lessonManager.tutor = tutorInstance.GetComponent<TutorController>();
            lessonManager.ui = canvasInstance.GetComponent<UIManager>();
            lessonManager.levelManager = levelManager;
            lessonManager.recognizerBehaviour = recognizer;

            var bootstrap = managerGO.GetComponent<LessonBootstrap>() ?? managerGO.AddComponent<LessonBootstrap>();
            bootstrap.lessonManager = lessonManager;
            bootstrap.levelToStart = level1;

            WireButton(canvasInstance.transform, "TestCorrectButton", recognizer.TestCorrect);
            WireButton(canvasInstance.transform, "TestWrongButton", recognizer.TestWrong);

            EditorUtility.SetDirty(managerGO);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[SignMasterVR] Scene wired. Press Play, then press C (test correct) / X (test wrong), or click the on-screen buttons, to walk through all 26 letters. Remember to save the scene (Ctrl+S).");
        }

        // ------------------------------------------------------------------
        // STEP 5 — Improve the existing Player rig (stationary, positioned,
        // facing the tutor). Does not create a Player — only touches one if
        // it's already in the scene (yours is the VR template's
        // "Complete XR Origin Set Up Hands Variant" prefab, confirmed by
        // reading your project).
        // ------------------------------------------------------------------
        [MenuItem("Tools/SignMasterVR/5 Improve Player Rig (stationary, positioned)")]
        public static void ImprovePlayerRig()
        {
            if (!RequireActiveSceneIsNotMainMenu()) return;

            GameObject player = GameObject.Find("Player");
            if (player == null)
            {
                Debug.LogWarning("[SignMasterVR] No GameObject named \"Player\" found in the open scene — nothing to improve. Make sure the VR template's Player/XR Origin rig has already been added to this scene.");
                return;
            }

            // Stand the learner on the LearnerStandingMark, facing the tutor (the tutor sits at
            // z=+2.5 facing -Z, so the player should sit at the mark facing +Z — Unity's default
            // forward, so no rotation needed once we zero it out).
            Vector3 standMark = new Vector3(0f, player.transform.position.y, -2f);
            player.transform.position = standMark;
            player.transform.rotation = Quaternion.identity;

            // Was: a list of guessed GameObject names ("continuousmoveprovider" and friends),
            // normalized and matched. It never disabled anything, because the rig's objects are
            // actually called "Move", "Turn", "Gravity", "Teleportation", "Climb", "Grab Move"
            // and "Jump". A silent no-op that logged "Disabled 0 locomotion object(s)" and read
            // like success. Matching on the component type cannot drift like that.
            int disabledCount = DisableLocomotion(player.transform, SceneManager.GetActiveScene());

            // Make sure the Lesson UI actually points at this rig's real camera rather than
            // relying on the "MainCamera" tag, which some XR template versions don't set.
            GameObject canvasInstance = GameObject.Find("LessonCanvas");
            Camera cam = player.GetComponentInChildren<Camera>(true);
            if (canvasInstance != null && cam != null)
            {
                var canvas = canvasInstance.GetComponent<Canvas>();
                if (canvas != null) canvas.worldCamera = cam;
            }

            EditorUtility.SetDirty(player);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[SignMasterVR] Player moved to the standing mark and rotated to face the tutor. Disabled {disabledCount} locomotion provider(s) by component type" +
                (cam != null ? $"; Lesson UI now points at \"{cam.name}\"." : ". No camera found under Player — check the rig manually.") +
                " A count of 0 now genuinely means there was nothing left to disable, rather than that the search missed.");
        }

        // ------------------------------------------------------------------
        // STEP 6 — Main Menu scene (Start Lesson / Levels / Reset Progress)
        // ------------------------------------------------------------------
        [MenuItem("Tools/SignMasterVR/6 Build Main Menu Scene")]
        public static void BuildMainMenuScene()
        {
            LevelData level1 = AssetDatabase.LoadAssetAtPath<LevelData>(Level1Path);
            if (level1 == null)
            {
                Debug.LogError("[SignMasterVR] Run Step 1 first — Level 1 data not found.");
                return;
            }

            EnsureFolder("Assets/Scenes");
            const string scenePath = "Assets/Scenes/MainMenu.unity";

            // Build the menu in its own scene, loaded ADDITIVELY so your currently open scene
            // (e.g. ClassRoom) and any unsaved work in it are never touched or discarded.
            Scene menuScene = default;
            bool wasAlreadyOpen = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.path == scenePath) { menuScene = s; wasAlreadyOpen = true; break; }
            }
            bool createdNewScene = false;
            if (!wasAlreadyOpen)
            {
                menuScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                createdNewScene = true;
            }

            // Everything below is wrapped so that no matter what happens — including an error we
            // haven't seen before — this additive scene ALWAYS gets saved-and-closed at the end
            // instead of being left behind as a stray, unsaved "Untitled" scene sitting open next
            // to whatever you were actually working on (which is what happened previously).
            try
            {
                // Clear the Inspector/Hierarchy selection before touching anything. Root cause of
                // the recurring "no Canvas attached to MenuCanvas" MissingComponentException:
                // if a GameObject named "MenuCanvas" was selected in a PREVIOUS run (Unity restores
                // the last selection when you reopen a project), the Inspector can try to redraw
                // that stale selection mid-script — while our new "MenuCanvas" object exists as a
                // bare RectTransform for a moment before Canvas is attached — and throw exactly
                // this exception. Clearing selection removes that window entirely.
                Selection.activeObject = null;

                if (FindInScene(menuScene, "Main Camera") == null)
                {
                    GameObject camGO = new GameObject("Main Camera");
                    var cam = camGO.AddComponent<Camera>();
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = MenuNearBlack;
                    camGO.tag = "MainCamera";
                    SceneManager.MoveGameObjectToScene(camGO, menuScene);
                }

                if (FindInScene(menuScene, "EventSystem") == null)
                {
                    GameObject es = new GameObject("EventSystem");
                    es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                    // Deliberately NOT StandaloneInputModule. This project has Active Input
                    // Handling set to "Input System Package (New)", so the legacy module throws
                    // InvalidOperationException on every frame it ticks. UpgradeSceneForVr below
                    // attaches XRUIInputModule instead.
                    SceneManager.MoveGameObjectToScene(es, menuScene);
                }

                GameObject canvasGO = FindInScene(menuScene, "MenuCanvas");
                if (canvasGO == null)
                {
                    // Create RectTransform + Canvas + CanvasScaler + GraphicRaycaster all in ONE
                    // GameObject(...) call instead of AddComponent-ing them one at a time. This
                    // makes the object atomic: there is never a frame where "MenuCanvas" exists
                    // with a RectTransform but no Canvas yet for something else (Inspector, Scene
                    // view Rect tool) to catch mid-construction.
                    canvasGO = new GameObject("MenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                    SceneManager.MoveGameObjectToScene(canvasGO, menuScene);
                }
                var canvas = canvasGO.GetComponent<Canvas>();
                if (canvas == null)
                    throw new System.InvalidOperationException("MenuCanvas exists but has no Canvas component even after atomic creation — something outside this script is removing it. Check for other Editor tools/scripts touching \"MenuCanvas\".");
                // Built screen-space because CreateText/CreateButton below lay everything out
                // against a 1280x720 reference resolution and that is simplest to reason about.
                // UpgradeSceneForVr converts it to World Space at the end of this method, which
                // keeps that layout pixel-for-pixel. Do NOT leave it screen-space: a
                // ScreenSpaceOverlay canvas renders as pure black in a headset.
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvasGO.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 720);
                RectTransform canvasRect = canvasGO.GetComponent<RectTransform>();
                Debug.Log("[SignMasterVR] checkpoint: MenuCanvas + Canvas/Scaler/Raycaster created OK.");

                CreateText(canvasRect, "TitleText", "SIGNMASTER VR", 56, TextAlignmentOptions.Center,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -110), new Vector2(800, 100), NeonCyan);
                CreateText(canvasRect, "SubtitleText", "Learn South African Sign Language", 22, TextAlignmentOptions.Center,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -180), new Vector2(800, 40), NeonCyan);

                Button startBtn = CreateButton(canvasRect, "StartLessonButton", "START LESSON",
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(320, 64), MenuPanelDark, NeonCyan);
                Button levelsBtn = CreateButton(canvasRect, "LevelsButton", "LEVELS",
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -50), new Vector2(320, 64), MenuPanelDark, NeonCyan);
                // Reset Progress is the one deliberately styled in neon pink -- a destructive action
                // gets the accent color, doubling as the "dash of pink" cosmetic ask.
                Button resetBtn = CreateButton(canvasRect, "ResetProgressButton", "RESET PROGRESS",
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 50), new Vector2(280, 44), NeonPink, Color.white);

                GameObject levelListPanel = CreatePanel(canvasRect, "LevelListPanel", new Color(0.02f, 0.02f, 0.05f, 0.92f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500, 400));
                RectTransform listRect = levelListPanel.GetComponent<RectTransform>();
                Button level1Btn = CreateButton(listRect, "Level1Button", "LEVEL 1 - LETTERS",
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -70), new Vector2(400, 60), MenuPanelDark, NeonCyan);
                TMP_Text level1Status = CreateText(listRect, "Level1StatusText", "", 16, TextAlignmentOptions.Center,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -115), new Vector2(400, 30), NeonCyan);
                Button backBtn = CreateButton(listRect, "BackButton", "BACK",
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(160, 44), MenuPanelDark, NeonCyan);
                levelListPanel.SetActive(false);
                Debug.Log("[SignMasterVR] checkpoint: all text/buttons/panels created OK.");

                GameObject menuSystem = FindInScene(menuScene, "MenuSystem");
                if (menuSystem == null) { menuSystem = new GameObject("MenuSystem"); SceneManager.MoveGameObjectToScene(menuSystem, menuScene); }
                if (menuSystem.GetComponent<ProgressManager>() == null) menuSystem.AddComponent<ProgressManager>();

                if (canvasGO.GetComponent<Canvas>() == null)
                    throw new System.InvalidOperationException("MenuCanvas lost its Canvas component sometime between initial creation and MainMenuController wiring — this narrows the bug to something in the CreateText/CreateButton/CreatePanel calls above.");
                var menuController = canvasGO.GetComponent<MainMenuController>() ?? canvasGO.AddComponent<MainMenuController>();
                menuController.levels = new[] { level1 };
                menuController.classroomSceneName = "ClassRoom";
                menuController.levelListPanel = levelListPanel;
                menuController.levelRows = new[] { new MainMenuController.LevelRow { level = level1, statusText = level1Status } };
                Debug.Log("[SignMasterVR] checkpoint: MainMenuController added and wired OK.");

                WireButton(canvasRect, "StartLessonButton", menuController.StartFirstLevel);
                WireButton(canvasRect, "LevelsButton", menuController.ShowLevelList);
                WireButton(canvasRect, "ResetProgressButton", menuController.ResetProgress);
                WireButton(listRect, "Level1Button", menuController.StartFirstLevel);
                WireButton(listRect, "BackButton", menuController.HideLevelList);

                // Make it a VR scene before it ever hits disk. Without this the menu is a flat
                // 2D panel that looks right in the Editor and is invisible on a Quest.
                UpgradeSceneForVr(menuScene, isMenu: true);

                Debug.Log("[SignMasterVR] Main Menu hierarchy built, saving...");
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[SignMasterVR] Building the Main Menu scene hit an error partway through — see the exception below. The scene will still be saved (with whatever was built so far) and closed rather than left open and unsaved.");
                Debug.LogException(ex);
            }
            finally
            {
                bool saved = EditorSceneManager.SaveScene(menuScene, scenePath);
                if (!saved)
                    Debug.LogError($"[SignMasterVR] EditorSceneManager.SaveScene reported failure for {scenePath}. Check for additional Unity errors above, and that Assets/Scenes/ isn't read-only or locked by another process (e.g. OneDrive sync).");
                else
                    Debug.Log($"[SignMasterVR] Main Menu scene saved at {scenePath}. Open it directly (with ClassRoom NOT also loaded) and press Play to test, or add both MainMenu (first) and ClassRoom to File > Build Profiles > Scenes In Build once you're ready for an actual build.");

                if (createdNewScene) EditorSceneManager.CloseScene(menuScene, true);
            }
        }

        // ------------------------------------------------------------------
        // STEP 7 — Fix an imported avatar dropped into Tutor.prefab as a
        // child named "character" (e.g. from Mixamo): converts any
        // Built-in-shader materials to URP Lit, generates a proper Humanoid
        // Avatar from the model and assigns it to the Tutor's Animator, and
        // plants the character's feet on the floor using its actual mesh
        // bounds instead of a guessed position. Run this once after you've
        // dragged an avatar into Tutor.prefab in place of the primitives.
        // ------------------------------------------------------------------
        [MenuItem("Tools/SignMasterVR/7 Fix Tutor Avatar (materials + rig + ground)")]
        public static void FixTutorAvatar()
        {
            GameObject tutorAsset = AssetDatabase.LoadAssetAtPath<GameObject>(TutorPrefabPath);
            if (tutorAsset == null)
            {
                Debug.LogError($"[SignMasterVR] No prefab found at {TutorPrefabPath} — run Step 2 first.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(TutorPrefabPath);
            try
            {
                Transform character = root.transform.Find("character");
                if (character == null)
                {
                    Debug.LogError("[SignMasterVR] No child named \"character\" under Tutor.prefab's root. This tool expects the avatar you dragged in to still be named \"character\" — rename it back, or tell me the actual name and I'll adjust the script.");
                    return;
                }

                // ---- 1. Fix the source FBX's Rig import settings and assign a real Avatar ----
                // NOTE: this used to set the rig to Humanoid. Humanoid worked for the ground-fit
                // math, but at runtime Unity reconstructs the pose through the avatar's "muscle"
                // space using its auto-generated bone mapping — and for this auto-mapped Mixamo
                // rig, that reconstruction comes out hunched/crouched instead of standing upright
                // (a well-known issue with "Create From This Model" auto-mapping on Mixamo
                // skeletons). None of our clips need Humanoid retargeting anyway — TutorIdleClip /
                // TutorActiveClip only rotate the "Head" child by its literal Transform path — so
                // Generic sidesteps muscle reconstruction entirely and just plays the real imported
                // skeleton, standing pose intact.
                string fbxPath = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(character.gameObject));
                if (string.IsNullOrEmpty(fbxPath))
                {
                    Debug.LogWarning("[SignMasterVR] Couldn't resolve the source FBX path from \"character\" — skipping the Rig/Avatar fix, still fixing materials and ground position below.");
                }
                else
                {
                    var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                    if (importer != null)
                    {
                        bool changed = false;
                        if (importer.animationType != ModelImporterAnimationType.Generic) { importer.animationType = ModelImporterAnimationType.Generic; changed = true; }
                        if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; changed = true; }
                        if (changed)
                        {
                            EditorUtility.SetDirty(importer);
                            importer.SaveAndReimport();
                            Debug.Log($"[SignMasterVR] {fbxPath}: Rig set to Generic / Create From This Model, reimported (switched away from Humanoid — see comment above).");
                        }

                        Avatar avatar = null;
                        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                            if (asset is Avatar a) { avatar = a; break; }

                        Animator animator = root.GetComponent<Animator>();
                        if (animator != null)
                        {
                            // A Generic Avatar is optional — our clips target this same hierarchy
                            // directly by path and don't need one — but assign it if one exists,
                            // since it doesn't hurt and Unity generates one by default anyway.
                            animator.avatar = avatar;
                            Debug.Log(avatar != null
                                ? $"[SignMasterVR] Assigned generated Generic Avatar \"{avatar.name}\" to the Tutor's Animator."
                                : "[SignMasterVR] No Avatar sub-asset found after reimport — that's fine for Generic, the Animator will play clips directly off the hierarchy.");
                        }
                    }
                }

                // ---- 2. Convert any Built-in-shader materials on the character to URP Lit ----
                Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
                int converted = 0;
                foreach (var renderer in character.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = renderer.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        Material m = mats[i];
                        if (m == null || m.shader == null) continue;
                        if (m.shader == urpLit) continue;
                        if (m.shader.name.StartsWith("Universal Render Pipeline")) continue; // already some URP variant

                        Material fixedMat = new Material(urpLit) { name = m.name + " (URP)" };
                        if (m.HasProperty("_Color")) fixedMat.SetColor("_BaseColor", m.GetColor("_Color"));
                        if (m.HasProperty("_MainTex"))
                        {
                            Texture mainTex = m.GetTexture("_MainTex");
                            if (mainTex != null) fixedMat.SetTexture("_BaseMap", mainTex);
                        }

                        string matFolder = string.IsNullOrEmpty(fbxPath) ? PrefabFolder : Path.GetDirectoryName(fbxPath).Replace("\\", "/");
                        EnsureFolder(matFolder);
                        string matPath = AssetDatabase.GenerateUniqueAssetPath($"{matFolder}/{m.name}_URP.mat");
                        AssetDatabase.CreateAsset(fixedMat, matPath);
                        mats[i] = fixedMat;
                        converted++;
                    }
                    renderer.sharedMaterials = mats;
                }
                Debug.Log(converted > 0
                    ? $"[SignMasterVR] Converted {converted} material(s) on the Tutor avatar from a Built-in shader to Universal Render Pipeline/Lit."
                    : "[SignMasterVR] No Built-in-shader materials found on the Tutor avatar — nothing needed converting.");

                // ---- 2b. Report exactly what texture (if any) each material actually has —
                // if the avatar still looks white/flat after the shader check above passes,
                // the real cause is almost always a missing Base Map texture, not the shader,
                // and this makes that visible instead of guessing.
                var seenMats = new HashSet<Material>();
                foreach (var renderer in character.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var m in renderer.sharedMaterials)
                    {
                        if (m == null || !seenMats.Add(m)) continue;
                        Texture baseTex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap")
                            : m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                        Debug.Log($"[SignMasterVR] Material \"{m.name}\" — shader: {m.shader?.name ?? "(none)"}, Base Map texture: {(baseTex != null ? baseTex.name : "NONE — this is why it renders flat/white")}");
                    }
                }

                // ---- 3. Plant the character's feet on the floor using each mesh's actual
                // bind-pose geometry — NOT Renderer.bounds. Renderer.bounds requires at
                // least one real render/skinning update to be accurate, and this tool runs
                // against a prefab loaded into a disconnected off-screen scene
                // (PrefabUtility.LoadPrefabContents) that never actually renders a frame,
                // so for a SkinnedMeshRenderer that value can come back stale/near-zero.
                // That's exactly what happened last run: it computed only a ~0.125m shift
                // for a full adult-height character, leaving it sunk to the hips. Using
                // each mesh's own bind-pose bounds (SkinnedMeshRenderer.localBounds /
                // MeshFilter.sharedMesh.bounds), transformed into world space by hand,
                // doesn't depend on anything having been rendered first.
                bool anyMesh = false;
                Bounds combined = new Bounds();
                var smrList = character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var mfList = character.GetComponentsInChildren<MeshFilter>(true);
                Debug.Log($"[SignMasterVR] Ground-fit scan under \"character\": {smrList.Length} SkinnedMeshRenderer(s), {mfList.Length} MeshFilter(s).");
                foreach (var smr in smrList)
                {
                    Bounds world = TransformBoundsToWorld(smr.localBounds, smr.transform);
                    Debug.Log($"[SignMasterVR]   SkinnedMeshRenderer \"{smr.name}\" — localBounds center={smr.localBounds.center}, extents={smr.localBounds.extents} -> world min.y={world.min.y:F3}, max.y={world.max.y:F3}");
                    if (!anyMesh) { combined = world; anyMesh = true; }
                    else combined.Encapsulate(world);
                }
                foreach (var mf in mfList)
                {
                    if (mf.sharedMesh == null) continue;
                    Bounds world = TransformBoundsToWorld(mf.sharedMesh.bounds, mf.transform);
                    Debug.Log($"[SignMasterVR]   MeshFilter \"{mf.name}\" — mesh bounds center={mf.sharedMesh.bounds.center}, extents={mf.sharedMesh.bounds.extents} -> world min.y={world.min.y:F3}, max.y={world.max.y:F3}");
                    if (!anyMesh) { combined = world; anyMesh = true; }
                    else combined.Encapsulate(world);
                }

                if (anyMesh)
                {
                    // The prefab's root sits at local (0,0,0) with no rotation/scale while loaded
                    // this way, so world-space bounds.min.y is directly how far below (or above)
                    // "character"'s own current position its lowest point sits. Computed fresh
                    // from the current position every run, so this self-corrects no matter how
                    // far off (or how many times already shifted) the previous run left it.
                    float lowestY = combined.min.y;
                    float oldY = character.localPosition.y;
                    float newY = oldY - lowestY;
                    character.localPosition = new Vector3(character.localPosition.x, newY, character.localPosition.z);
                    Debug.Log($"[SignMasterVR] Shifted \"character\" local Y from {oldY:F3} to {newY:F3} so its lowest point (measured from bind-pose mesh geometry, was at world Y={lowestY:F3}) sits on the floor (y=0).");
                }
                else
                {
                    Debug.LogWarning("[SignMasterVR] No SkinnedMeshRenderer/MeshFilter meshes found under \"character\" — couldn't compute ground position automatically.");
                }

                PrefabUtility.SaveAsPrefabAsset(root, TutorPrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[SignMasterVR] Tutor.prefab saved with the fixes above.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------------
        // STEP 7b — Read-only measurement. Step 7's ground-fit math (both the
        // Renderer.bounds version and the bind-pose-localBounds version) is
        // computed against a prefab loaded into a disconnected off-screen
        // scene via PrefabUtility.LoadPrefabContents, which never actually
        // gets rendered — first it under-shot (0.125m, avatar sunk to the
        // hips), then over-shot (1.227m, avatar floating in the air) once the
        // bind-pose calc was swapped in, which says the disconnected-scene
        // numbers plain can't be trusted for this rig. This instead measures
        // the REAL "Tutor" object sitting in your currently open scene, which
        // Unity actually renders (Scene view redraws it every frame in Edit
        // Mode already — Play Mode isn't required, though it's fine too),
        // so Renderer.bounds is accurate here. It only logs numbers — it
        // does not change anything. Run it with ClassRoom.unity open and the
        // Tutor visible, then send Claude the Console line it prints.
        // ------------------------------------------------------------------
        [MenuItem("Tools/SignMasterVR/7b Measure Tutor Ground Offset (read-only)")]
        public static void MeasureTutorGroundOffset()
        {
            GameObject tutor = GameObject.Find("Tutor");
            if (tutor == null)
            {
                Debug.LogError("[SignMasterVR] No GameObject named \"Tutor\" found in the currently open scene. Open ClassRoom.unity and make sure Tutor is in the Hierarchy (Play Mode or Edit Mode both work).");
                return;
            }
            Transform character = tutor.transform.Find("character");
            if (character == null)
            {
                Debug.LogError("[SignMasterVR] Tutor has no child named \"character\" in the open scene.");
                return;
            }
            var renderers = character.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Debug.LogError("[SignMasterVR] No Renderers found under \"character\" — can't measure.");
                return;
            }
            Bounds combined = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) combined.Encapsulate(renderers[i].bounds);

            const float floorY = 0f; // ClassRoom's floor Plane sits at world Y=0
            float feetY = combined.min.y;
            float gap = feetY - floorY; // positive = floating above the floor, negative = sunk below it
            float currentLocalY = character.localPosition.y;
            float suggestedLocalY = currentLocalY - gap;

            string state = gap > 0.01f ? $"floating {gap:F4}m ABOVE the floor" : gap < -0.01f ? $"sunk {-gap:F4}m BELOW the floor" : "already essentially on the floor";
            Debug.Log($"[SignMasterVR] MEASURED (live, {(Application.isPlaying ? "Play Mode" : "Edit Mode")}, real rendered scene — not the disconnected prefab-editing scene): " +
                $"character's lowest point is at world Y={feetY:F4}, so it's {state}. " +
                $"Current \"character\" local Y = {currentLocalY:F4}. Exact local Y needed to sit flush on the floor = {suggestedLocalY:F4}. " +
                "Send Claude this Console line (or just the last number) and it'll write that exact value straight into Tutor.prefab.");
        }

        // ------------------------------------------------------------------
        // STEP 8 — Wire the real Mixamo textures into Ch07_body / Ch07_hair.
        // FBX downloads from Mixamo never include actual texture image files
        // (only material name references) — that's why Step 7 logged "Base
        // Map texture: NONE" and the avatar renders flat white. The fix is a
        // Collada (.dae) re-download of the same character, which bundles a
        // real Textures folder. Drop those PNGs into
        // Assets/Texture_and_materials/Character/ named:
        //   Ch07_body_diffuse.png, Ch07_body_normal.png, Ch07_body_mask.png,
        //   Ch07_hair_diffuse.png, Ch07_hair_normal.png
        // then run this once. Safe to re-run.
        // ------------------------------------------------------------------
        [MenuItem("Tools/SignMasterVR/8 Assign Character Textures")]
        public static void AssignCharacterTextures()
        {
            GameObject tutorAsset = AssetDatabase.LoadAssetAtPath<GameObject>(TutorPrefabPath);
            if (tutorAsset == null)
            {
                Debug.LogError($"[SignMasterVR] No prefab found at {TutorPrefabPath} — run Step 2 (and Step 7) first.");
                return;
            }

            Texture2D bodyDiffuse = LoadTexture($"{CharacterTextureFolder}/Ch07_body_diffuse.png");
            Texture2D bodyNormal = SetupAsNormalMap($"{CharacterTextureFolder}/Ch07_body_normal.png");
            Texture2D bodyMask = SetupAsLinear($"{CharacterTextureFolder}/Ch07_body_mask.png");
            Texture2D hairDiffuse = LoadTexture($"{CharacterTextureFolder}/Ch07_hair_diffuse.png");
            Texture2D hairNormal = SetupAsNormalMap($"{CharacterTextureFolder}/Ch07_hair_normal.png");

            if (bodyDiffuse == null && hairDiffuse == null)
            {
                Debug.LogError($"[SignMasterVR] No texture files found in {CharacterTextureFolder} — drop Ch07_body_diffuse.png / Ch07_body_normal.png / Ch07_body_mask.png / Ch07_hair_diffuse.png / Ch07_hair_normal.png there first, then re-run this.");
                return;
            }

            // ---- PASS 1: find any body/hair materials still embedded INSIDE the FBX
            // (sub-assets, not their own files) and extract them to standalone .mat
            // files, fully unloading the prefab-contents scene in between. Otherwise
            // the very next FBX reimport (e.g. any future Rig setting change)
            // regenerates fresh materials from scratch and silently wipes out
            // whatever textures we assign — which is exactly what just happened when
            // Step 7's Generic-rig switch reimported the model and the avatar went
            // flat white again. Doing the extraction+reimport as its own pass, with
            // nothing holding a reference into the prefab's loaded hierarchy while it
            // happens, avoids mutating objects out from under an in-progress loop.
            var toExtract = new List<(string matName, string fbxPath)>();
            {
                GameObject scanRoot = PrefabUtility.LoadPrefabContents(TutorPrefabPath);
                try
                {
                    Transform scanCharacter = scanRoot.transform.Find("character");
                    if (scanCharacter == null)
                    {
                        Debug.LogError("[SignMasterVR] No child named \"character\" under Tutor.prefab's root — run Step 7's avatar swap first.");
                        return;
                    }
                    var seen = new HashSet<string>();
                    foreach (var renderer in scanCharacter.GetComponentsInChildren<Renderer>(true))
                    {
                        foreach (var m in renderer.sharedMaterials)
                        {
                            if (m == null || !seen.Add(m.name)) continue;
                            bool isBody = m.name.IndexOf("body", System.StringComparison.OrdinalIgnoreCase) >= 0;
                            bool isHair = m.name.IndexOf("hair", System.StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!isBody && !isHair) continue;
                            string matAssetPath = AssetDatabase.GetAssetPath(m);
                            if (!string.IsNullOrEmpty(matAssetPath) && matAssetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                                toExtract.Add((m.name, matAssetPath));
                        }
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(scanRoot);
                }
            }

            foreach (var (matName, fbxPath) in toExtract)
            {
                // Re-fetch the material fresh each time (rather than reusing anything from
                // the unloaded scratch scene above) — after each extraction+reimport the
                // FBX's other embedded sub-assets can be regenerated too.
                Material embedded = null;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                    if (asset is Material mm && mm.name == matName) { embedded = mm; break; }
                if (embedded == null) continue; // already extracted (or gone) — nothing to do

                EnsureFolder(CharacterTextureFolder);
                string extractPath = AssetDatabase.GenerateUniqueAssetPath($"{CharacterTextureFolder}/{matName}.mat");
                string error = AssetDatabase.ExtractAsset(embedded, extractPath);
                if (string.IsNullOrEmpty(error))
                {
                    // Unity's own documented pattern for ExtractAsset: commit the parent
                    // model's import settings and force it to reimport so every instance
                    // (including the one nested in Tutor.prefab) picks up the new external
                    // material reference automatically.
                    AssetDatabase.WriteImportSettingsIfDirty(fbxPath);
                    AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
                    Debug.Log($"[SignMasterVR] Extracted \"{matName}\" out of the FBX into a standalone material at {extractPath} — future FBX reimports (Rig changes, etc.) won't wipe its textures anymore.");
                }
                else
                {
                    Debug.LogWarning($"[SignMasterVR] Couldn't extract material \"{matName}\" from the FBX ({error}) — texture assignment below will still work now but may be lost on a future FBX reimport.");
                }
            }

            // ---- PASS 2: wire textures into whatever the body/hair materials are now
            // (standalone extracted .mat files after the pass above, or still-embedded
            // ones if extraction wasn't possible for some reason) ----
            GameObject root = PrefabUtility.LoadPrefabContents(TutorPrefabPath);
            try
            {
                Transform character = root.transform.Find("character");
                if (character == null)
                {
                    Debug.LogError("[SignMasterVR] No child named \"character\" under Tutor.prefab's root — run Step 7's avatar swap first.");
                    return;
                }

                int wired = 0;
                var seenMats = new HashSet<Material>();
                foreach (var renderer in character.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var m in renderer.sharedMaterials)
                    {
                        if (m == null || !seenMats.Add(m)) continue;

                        bool isBody = m.name.IndexOf("body", System.StringComparison.OrdinalIgnoreCase) >= 0;
                        bool isHair = m.name.IndexOf("hair", System.StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!isBody && !isHair) continue;

                        if (isBody)
                        {
                            if (bodyDiffuse != null && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", bodyDiffuse);
                            if (bodyNormal != null && m.HasProperty("_BumpMap"))
                            {
                                m.SetTexture("_BumpMap", bodyNormal);
                                m.EnableKeyword("_NORMALMAP");
                            }
                            if (bodyMask != null && m.HasProperty("_OcclusionMap")) m.SetTexture("_OcclusionMap", bodyMask);
                        }
                        else
                        {
                            if (hairDiffuse != null && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", hairDiffuse);
                            if (hairNormal != null && m.HasProperty("_BumpMap"))
                            {
                                m.SetTexture("_BumpMap", hairNormal);
                                m.EnableKeyword("_NORMALMAP");
                            }
                            // Hair cards need the transparent parts of the texture cut away
                            // instead of drawn as solid quads — same as ticking "Alpha Clipping"
                            // by hand in the URP Lit inspector.
                            if (m.HasProperty("_AlphaClip"))
                            {
                                m.SetFloat("_AlphaClip", 1f);
                                m.EnableKeyword("_ALPHATEST_ON");
                                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                            }
                            if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.5f);
                            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f); // double-sided, or hair cards vanish from certain angles
                        }

                        EditorUtility.SetDirty(m);
                        wired++;
                        Debug.Log($"[SignMasterVR] Wired textures into material \"{m.name}\" (asset: {AssetDatabase.GetAssetPath(m)}).");
                    }
                }

                if (wired == 0)
                {
                    Debug.LogWarning("[SignMasterVR] Found the \"character\" avatar but no material with \"body\" or \"hair\" in its name (expected \"Ch07_body\" / \"Ch07_hair\", as logged by Step 7) — nothing was wired. Tell me the actual material names if they differ and I'll adjust the script.");
                }
                else
                {
                    PrefabUtility.SaveAsPrefabAsset(root, TutorPrefabPath);
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[SignMasterVR] Tutor.prefab saved with textures wired into {wired} material(s). Look in the Scene view — the avatar should now show real skin/hair instead of flat white.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Transforms a local-space AABB into a world-space AABB by transforming all 8 corners through `t` — safe to use on a Bounds that came from mesh/import data rather than a live-rendered Renderer.</summary>
        private static Bounds TransformBoundsToWorld(Bounds local, Transform t)
        {
            Vector3 c = local.center;
            Vector3 e = local.extents;
            Vector3 min = t.TransformPoint(c + new Vector3(-e.x, -e.y, -e.z));
            Vector3 max = min;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 corner = t.TransformPoint(c + new Vector3(sx * e.x, sy * e.y, sz * e.z));
                        min = Vector3.Min(min, corner);
                        max = Vector3.Max(max, corner);
                    }
            Bounds b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }

        private static Texture2D LoadTexture(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        /// <summary>Marks the PNG at `path` as a Normal Map (if it exists and isn't already) and reimports, so Unity decodes it correctly instead of treating it as a plain color texture.</summary>
        private static Texture2D SetupAsNormalMap(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return null;
            if (importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Marks the PNG at `path` as linear (not sRGB) — for grayscale data maps like a mask/specular map, where color-space conversion would skew the values.</summary>
        private static Texture2D SetupAsLinear(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return null;
            if (importer.sRGBTexture)
            {
                importer.sRGBTexture = false;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------
        // STEP 9 — Assign alphabet reference images (Letter_A.png..Letter_Z.png
        // in Assets/Texture_and_materials/Alphabet/) to each letter's
        // GestureData.referenceImage. UIManager.ShowGesturePrompt already reads
        // this field — it's just been empty until now. Safe to re-run.
        // ------------------------------------------------------------------
        [MenuItem("Tools/SignMasterVR/9 Assign Alphabet Reference Images")]
        public static void AssignAlphabetReferenceImages()
        {
            int assigned = 0, missingImage = 0, missingGesture = 0;
            for (char c = 'A'; c <= 'Z'; c++)
            {
                string letter = c.ToString();
                string gesturePath = $"{GestureFolder}/Gesture_{letter}.asset";
                GestureData gesture = AssetDatabase.LoadAssetAtPath<GestureData>(gesturePath);
                if (gesture == null)
                {
                    missingGesture++;
                    continue;
                }

                string imgPath = $"{AlphabetImageFolder}/Letter_{letter}.png";
                var importer = AssetImporter.GetAtPath(imgPath) as TextureImporter;
                if (importer == null)
                {
                    missingImage++;
                    continue;
                }

                bool changed = false;
                if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; changed = true; }
                if (importer.spriteImportMode != SpriteImportMode.Single) { importer.spriteImportMode = SpriteImportMode.Single; changed = true; }
                if (changed)
                {
                    EditorUtility.SetDirty(importer);
                    importer.SaveAndReimport();
                }

                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(imgPath);
                if (sprite == null)
                {
                    Debug.LogWarning($"[SignMasterVR] {imgPath} didn't produce a Sprite after reimport — check the file imported correctly.");
                    continue;
                }

                gesture.referenceImage = sprite;
                EditorUtility.SetDirty(gesture);
                assigned++;
            }

            AssetDatabase.SaveAssets();
            string msg = $"[SignMasterVR] Assigned reference images to {assigned}/26 letters.";
            if (missingGesture > 0) msg += $" {missingGesture} letter(s) had no GestureData asset yet — run Step 1 first.";
            if (missingImage > 0) msg += $" {missingImage} letter(s) had no Letter_X.png in {AlphabetImageFolder} — drop the missing PNGs there and re-run.";
            Debug.Log(msg);
        }

        // ------------------------------------------------------------------
        // STEP 10 — Wire the real Mixamo idle animation into the "Idle"
        // Animator state (currently just a tiny placeholder head-wiggle),
        // so the tutor rests in a real pose instead of the T-pose.
        // ------------------------------------------------------------------
        private const string IdleFbxPath = AnimFolder + "/Idle.fbx";

        [MenuItem("Tools/SignMasterVR/10 Assign Real Idle Animation")]
        public static void AssignRealIdleAnimation()
        {
            var fbxImporter = AssetImporter.GetAtPath(IdleFbxPath) as ModelImporter;
            if (fbxImporter == null)
            {
                Debug.LogError($"[SignMasterVR] No FBX found at {IdleFbxPath}. Export an \"Idle\" animation from Mixamo (FBX for Unity, Without Skin) using the SAME character/rig session as character.fbx, and drop it there as Idle.fbx.");
                return;
            }

            bool changed = false;
            if (fbxImporter.animationType != ModelImporterAnimationType.Generic) { fbxImporter.animationType = ModelImporterAnimationType.Generic; changed = true; }
            if (fbxImporter.avatarSetup != ModelImporterAvatarSetup.NoAvatar) { fbxImporter.avatarSetup = ModelImporterAvatarSetup.NoAvatar; changed = true; }
            if (changed)
            {
                EditorUtility.SetDirty(fbxImporter);
                fbxImporter.SaveAndReimport();
            }

            AnimationClip idleClip = null;
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(IdleFbxPath))
            {
                if (obj is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    idleClip = clip;
                    break;
                }
            }

            if (idleClip == null)
            {
                Debug.LogError($"[SignMasterVR] {IdleFbxPath} imported but contained no AnimationClip. Re-check the Mixamo export included the animation (not just the skeleton).");
                return;
            }

            // Quick sanity check: this only plays correctly if it was exported
            // from the same rig session as character.fbx (bone names must
            // match exactly, e.g. "mixamorig8:Hips" on both).
            var bindings = AnimationUtility.GetCurveBindings(idleClip);
            bool looksCompatible = bindings.Length == 0;
            foreach (var b in bindings)
            {
                if (b.path.Contains("mixamorig") || b.path.Length == 0) { looksCompatible = true; break; }
            }
            if (!looksCompatible)
            {
                Debug.LogWarning($"[SignMasterVR] {IdleFbxPath}'s clip \"{idleClip.name}\" has curve paths that don't look like they match character.fbx's \"mixamorig8:\" skeleton. If the tutor doesn't move when you press Play, this animation was probably exported from a different Mixamo upload/session — re-download Idle from the same character.");
            }

            SetLooping(idleClip, true);
            EditorUtility.SetDirty(idleClip);

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorControllerPath);
            if (controller == null)
            {
                Debug.LogError($"[SignMasterVR] No AnimatorController found at {AnimatorControllerPath}. Run Step 2 first.");
                return;
            }

            var sm = controller.layers[0].stateMachine;
            var idleState = FindOrAddState(sm, "Idle", idleClip);
            controller.layers[0].stateMachine.defaultState = idleState;
            EditorUtility.SetDirty(controller);

            AssetDatabase.SaveAssets();
            Debug.Log($"[SignMasterVR] Idle state now plays \"{idleClip.name}\" ({bindings.Length} curves, looping). Press Play and give it a second for the Animator to settle into it.");
        }

        // ------------------------------------------------------------------
        // STEP 11 — Wire the three generic feedback stings ("Correct",
        // "TryAgain", "LevelComplete") into the AudioManager that Step 4 put
        // on "LessonSystem". GestureData already defaults every letter's
        // successAudioKey/tryAgainAudioKey to these same two keys, and
        // LessonManager.CompleteLevel() already calls Play("LevelComplete")
        // -- this step is purely "find the clip, add it to the list", no
        // other code changes needed.
        // ------------------------------------------------------------------
        private static readonly string[] FeedbackKeys = { "Correct", "TryAgain", "LevelComplete" };
        private static readonly string[] AudioExtensions = { ".wav", ".mp3", ".ogg" };

        [MenuItem("Tools/SignMasterVR/11 Assign Feedback Audio")]
        public static void AssignFeedbackAudio()
        {
            if (!RequireActiveSceneIsNotMainMenu()) return;

            GameObject managerGO = GameObject.Find("LessonSystem");
            if (managerGO == null)
            {
                Debug.LogError("[SignMasterVR] No \"LessonSystem\" GameObject in the active scene. Run Step 4 (Wire Up Current Scene) first, with ClassRoom.unity open.");
                return;
            }

            var audio = managerGO.GetComponent<AudioManager>();
            if (audio == null)
            {
                Debug.LogError("[SignMasterVR] \"LessonSystem\" has no AudioManager component. Run Step 4 first.");
                return;
            }

            int assigned = 0;
            var missing = new List<string>();

            foreach (var key in FeedbackKeys)
            {
                AudioClip clip = null;
                string foundPath = null;
                foreach (var ext in AudioExtensions)
                {
                    string candidate = $"{FeedbackAudioFolder}/{key}{ext}";
                    clip = AssetDatabase.LoadAssetAtPath<AudioClip>(candidate);
                    if (clip != null) { foundPath = candidate; break; }
                }

                if (clip == null)
                {
                    missing.Add(key);
                    continue;
                }

                int existingIndex = audio.clips.FindIndex(e => e.key == key);
                var entry = new AudioManager.ClipEntry { key = key, clip = clip };
                if (existingIndex >= 0) audio.clips[existingIndex] = entry;
                else audio.clips.Add(entry);

                assigned++;
                Debug.Log($"[SignMasterVR] \"{key}\" -> {foundPath}");
            }

            var existingSource = managerGO.GetComponent<AudioSource>();
            if (existingSource == null)
            {
                existingSource = managerGO.AddComponent<AudioSource>();
                existingSource.playOnAwake = false;
                Debug.Log("[SignMasterVR] No AudioSource on \"LessonSystem\" — added one.");
                EditorUtility.SetDirty(existingSource);
                EditorUtility.SetDirty(managerGO);
            }
            audio.source = existingSource;
            Debug.Log($"[SignMasterVR] AudioManager.source is now {(audio.source != null ? "assigned" : "STILL NULL -- this is a bug, tell Claude")} (instance id {(audio.source != null ? audio.source.GetInstanceID().ToString() : "n/a")}).");

            EditorUtility.SetDirty(audio);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            string msg = $"[SignMasterVR] Assigned {assigned}/{FeedbackKeys.Length} feedback clips to AudioManager. Remember to save the scene (Ctrl+S).";
            if (missing.Count > 0)
                msg += $" Missing: {string.Join(", ", missing)} -- drop {string.Join("/", missing)}.wav (or .mp3/.ogg) into {FeedbackAudioFolder}/ and re-run.";
            Debug.Log(msg);
        }

        // ------------------------------------------------------------------
        // STEP 12 — Restyle the ALREADY-BUILT Main Menu (neon cyan text,
        // near-black background, one pink accent on Reset Progress) in place.
        //
        // Deliberately NOT done by re-running Step 6: Step 6 only guards
        // against duplicating the top-level scene objects (Camera,
        // EventSystem, MenuCanvas) -- everything under MenuCanvas
        // (TitleText, buttons, the level list) gets created unconditionally
        // every time, so re-running Step 6 on a MainMenu that already has
        // content would duplicate every text/button in it. This step instead
        // finds each existing element by its known path and only touches
        // colors -- safe to run as many times as you like.
        //
        // The Lesson canvas doesn't need an equivalent step: Step 3 rebuilds
        // that whole prefab from scratch every time (no scene objects
        // involved), so just re-running Step 3 after this file recompiles
        // is enough to pick up its bigger size/fonts/cyan text.
        // ------------------------------------------------------------------
        [MenuItem("Tools/SignMasterVR/12 Apply Neon Menu Style")]
        public static void ApplyNeonMenuStyle()
        {
            const string scenePath = "Assets/Scenes/MainMenu.unity";
            Scene menuScene = default;
            bool wasAlreadyOpen = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.path == scenePath) { menuScene = s; wasAlreadyOpen = true; break; }
            }
            bool openedHere = false;
            if (!wasAlreadyOpen)
            {
                if (!System.IO.File.Exists(scenePath))
                {
                    Debug.LogError($"[SignMasterVR] No scene at {scenePath} -- run Step 6 first.");
                    return;
                }
                menuScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                openedHere = true;
            }

            try
            {
                GameObject canvasGO = FindInScene(menuScene, "MenuCanvas");
                if (canvasGO == null)
                {
                    Debug.LogError("[SignMasterVR] MainMenu.unity has no \"MenuCanvas\" -- run Step 6 first.");
                    return;
                }
                Transform canvasT = canvasGO.transform;

                var cam = FindInScene(menuScene, "Main Camera")?.GetComponent<Camera>();
                if (cam != null) cam.backgroundColor = MenuNearBlack;

                RestyleText(canvasT, "TitleText", NeonCyan);
                RestyleText(canvasT, "SubtitleText", NeonCyan);
                RestyleButton(canvasT, "StartLessonButton", MenuPanelDark, NeonCyan);
                RestyleButton(canvasT, "LevelsButton", MenuPanelDark, NeonCyan);
                RestyleButton(canvasT, "ResetProgressButton", NeonPink, Color.white);

                Transform listPanel = canvasT.Find("LevelListPanel");
                if (listPanel != null)
                {
                    var panelImg = listPanel.GetComponent<Image>();
                    if (panelImg != null) { panelImg.color = new Color(0.02f, 0.02f, 0.05f, 0.92f); EditorUtility.SetDirty(panelImg); }
                    RestyleButton(listPanel, "Level1Button", MenuPanelDark, NeonCyan);
                    RestyleText(listPanel, "Level1StatusText", NeonCyan);
                    RestyleButton(listPanel, "BackButton", MenuPanelDark, NeonCyan);
                }
                else
                {
                    Debug.LogWarning("[SignMasterVR] \"LevelListPanel\" not found under MenuCanvas -- skipped its contents.");
                }

                EditorUtility.SetDirty(canvasGO);
                EditorSceneManager.MarkSceneDirty(menuScene);
                Debug.Log("[SignMasterVR] Main Menu restyled (neon cyan text, near-black background, pink Reset Progress button).");
            }
            finally
            {
                bool saved = EditorSceneManager.SaveScene(menuScene, scenePath);
                if (!saved)
                    Debug.LogError($"[SignMasterVR] EditorSceneManager.SaveScene reported failure for {scenePath}.");
                if (openedHere) EditorSceneManager.CloseScene(menuScene, true);
            }
        }

        private static void RestyleText(Transform root, string childPath, Color color)
        {
            Transform t = root.Find(childPath);
            if (t == null) { Debug.LogWarning($"[SignMasterVR] \"{childPath}\" not found under {root.name} -- skipped."); return; }
            var tmp = t.GetComponent<TMP_Text>();
            if (tmp == null) { Debug.LogWarning($"[SignMasterVR] \"{childPath}\" has no TMP_Text -- skipped."); return; }
            tmp.color = color;
            EditorUtility.SetDirty(tmp);
        }

        private static void RestyleButton(Transform root, string childPath, Color bg, Color labelColor)
        {
            Transform t = root.Find(childPath);
            if (t == null) { Debug.LogWarning($"[SignMasterVR] \"{childPath}\" not found under {root.name} -- skipped."); return; }
            var img = t.GetComponent<Image>();
            if (img != null) { img.color = bg; EditorUtility.SetDirty(img); }
            var label = t.Find("Label")?.GetComponent<TMP_Text>();
            if (label != null) { label.color = labelColor; EditorUtility.SetDirty(label); }
        }

        // ------------------------------------------------------------------
        // STEP 13 — Switch LessonManager from FakeGestureRecognizer (keyboard
        // C/X + on-screen debug buttons) to NetworkGestureRecognizer, which
        // talks over the local network to GestureServer/server.py -- a
        // Python program that runs on a laptop, reads an external webcam,
        // tracks the learner's hand (MediaPipe), and reports back whether it
        // matches the current target sign. See GestureServer/README.md for
        // how to run the laptop side and find its IP address.
        //
        // FakeGestureRecognizer is left on "LessonSystem", just disabled --
        // re-run Step 4 any time to switch back to keyboard/button testing.
        // Not part of RUN ALL since it depends on Step 4 already having run.
        // ------------------------------------------------------------------
        [MenuItem("Tools/SignMasterVR/13 Switch To Network Gesture Recognizer")]
        public static void SwitchToNetworkGestureRecognizer()
        {
            if (!RequireActiveSceneIsNotMainMenu()) return;

            GameObject managerGO = GameObject.Find("LessonSystem");
            if (managerGO == null)
            {
                Debug.LogError("[SignMasterVR] No \"LessonSystem\" GameObject in the active scene. Run Step 4 (Wire Up Current Scene) first.");
                return;
            }

            var lessonManager = managerGO.GetComponent<LessonManager>();
            if (lessonManager == null)
            {
                Debug.LogError("[SignMasterVR] \"LessonSystem\" has no LessonManager component. Run Step 4 first.");
                return;
            }

            var networkRecognizer = managerGO.GetComponent<NetworkGestureRecognizer>() ?? managerGO.AddComponent<NetworkGestureRecognizer>();
            networkRecognizer.lessonManager = lessonManager;
            lessonManager.recognizerBehaviour = networkRecognizer;

            var fakeRecognizer = managerGO.GetComponent<FakeGestureRecognizer>();
            if (fakeRecognizer != null) fakeRecognizer.enabled = false;

            EditorUtility.SetDirty(managerGO);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[SignMasterVR] LessonManager now listens to NetworkGestureRecognizer instead of FakeGestureRecognizer. " +
                "Select LessonSystem in the Hierarchy and set Network Gesture Recognizer > Laptop Host / Laptop Port to match the " +
                "laptop running GestureServer/server.py, then save the scene (Ctrl+S). " +
                "The on-screen TEST CORRECT/TEST WRONG buttons and the C/X keys go quiet in this mode (LessonManager is no longer " +
                "listening to FakeGestureRecognizer's event) -- re-run Step 4 to switch back.");
        }

        // ------------------------------------------------------------------
        // STEP 14 — Register Level 2 (Phrases). make_unity_assets.py (run by
        // the ML team) already created Level_2_Phrases.asset with all 16
        // phrase GestureData assets referenced in order, but nothing added it
        // to LevelManager.allLevels — Step 4 only ever assigned Level 1. This
        // makes Level 2 reachable and gates it behind Level 1's completion.
        // ------------------------------------------------------------------
        [MenuItem("Tools/SignMasterVR/14 Register Level 2 (Phrases)")]
        public static void RegisterLevel2()
        {
            LevelData level1 = AssetDatabase.LoadAssetAtPath<LevelData>(Level1Path);
            LevelData level2 = AssetDatabase.LoadAssetAtPath<LevelData>(Level2Path);
            if (level1 == null || level2 == null)
            {
                Debug.LogError($"[SignMasterVR] Couldn't load {(level1 == null ? Level1Path : Level2Path)}. " +
                    "Run Step 1 first, and make sure the ML team's make_unity_assets.py has been run " +
                    "(it generates Level_2_Phrases.asset under Assets/Data/Levels/).");
                return;
            }

            GameObject managerGO = GameObject.Find("LessonSystem");
            if (managerGO == null)
            {
                Debug.LogError("[SignMasterVR] No \"LessonSystem\" GameObject in the active scene. Run Step 4 (Wire Up Current Scene) first.");
                return;
            }
            var levelManager = managerGO.GetComponent<LevelManager>();
            if (levelManager == null)
            {
                Debug.LogError("[SignMasterVR] \"LessonSystem\" has no LevelManager component. Run Step 4 first.");
                return;
            }

            if (level2.requiredPreviousLevel != level1)
            {
                level2.requiredPreviousLevel = level1;
                EditorUtility.SetDirty(level2);
            }

            bool alreadyRegistered = levelManager.allLevels != null && System.Array.IndexOf(levelManager.allLevels, level2) >= 0;
            if (!alreadyRegistered)
            {
                var levels = new List<LevelData>(levelManager.allLevels ?? new LevelData[0]);
                if (!levels.Contains(level1)) levels.Add(level1); // defensive -- Step 4 normally already put it there
                levels.Add(level2);
                levelManager.allLevels = levels.ToArray();
                EditorUtility.SetDirty(managerGO);
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log(alreadyRegistered
                ? "[SignMasterVR] Level 2 (Phrases) was already registered on LevelManager."
                : "[SignMasterVR] Level 2 (Phrases, 16 signs) added to LevelManager.allLevels, unlocked after Level 1 is completed. " +
                  "There's no level-select screen yet, so to test it directly: select LessonSystem, temporarily drag Level_2_Phrases " +
                  "onto LessonBootstrap's Level To Start, press Play, then set it back to Level_1_Letters when you're done. " +
                  "Save the scene (Ctrl+S).");
        }

        // ------------------------------------------------------------------
        // STEP 15 — Add Ghost Hand Demo. Wires the ML team's GhostHandPlayer.cs
        // (Assets/Scripts/Lessons/GhostHandPlayer.cs) into the scene: a child
        // object under the Tutor that draws a 2D hand-skeleton replay of
        // whatever sign is currently being asked for, sourced from
        // GestureServer/export_ghost_hand.py's output. Purely additive —
        // LessonManager.ghostHand is optional and every call is null-guarded,
        // so this is safe to run even before any clips have been exported.
        // ------------------------------------------------------------------
        [MenuItem("Tools/SignMasterVR/15 Add Ghost Hand Demo")]
        public static void AddGhostHandDemo()
        {
            if (!RequireActiveSceneIsNotMainMenu()) return;

            GameObject managerGO = GameObject.Find("LessonSystem");
            GameObject tutorInstance = GameObject.Find("Tutor");
            if (managerGO == null || tutorInstance == null)
            {
                Debug.LogError("[SignMasterVR] Need both \"LessonSystem\" and \"Tutor\" in the active scene. Run Step 4 (Wire Up Current Scene) first.");
                return;
            }
            var lessonManager = managerGO.GetComponent<LessonManager>();
            if (lessonManager == null)
            {
                Debug.LogError("[SignMasterVR] \"LessonSystem\" has no LessonManager component. Run Step 4 first.");
                return;
            }

            // The ghost used to be parented to the Tutor at local (0, 1.1, -0.6), with the
            // comment "in front of the tutor, roughly chest height, facing the player". That
            // was wrong in practice for two reasons. The Tutor sits at z = 4.8, hard against
            // the LearningBoard, so "in front of the tutor" is still six metres from a learner
            // standing at z = -2 -- a 0.35-scale hand at that range is a smudge. And with the
            // Tutor's rotation left at identity, the offset put it on the board side of the
            // avatar, where the body occludes it.
            //
            // The ghost is the actual demonstration -- there are no signing animations on the
            // avatar, so if the ghost is not visible, nothing shows the learner the sign. That
            // is why "the tutor doesn't sign the phrases to me": the phrase clips were playing
            // all along, just where nobody could see them.
            //
            // So: a root object, placed off the LearnerStandingMark rather than off the Tutor,
            // an arm's length in front of the learner and a little below eye level. Read from
            // the mark so moving the mark moves the ghost with it.
            Transform ghostTransform = null;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                ghostTransform = root.name == "GhostHandDemo" ? root.transform : FindDeep(root.transform, "GhostHandDemo");
                if (ghostTransform != null) break;
            }

            GameObject ghostGO = ghostTransform != null ? ghostTransform.gameObject : new GameObject("GhostHandDemo");

            // Unparent whatever it was under -- worldPositionStays: false, because the world
            // position is about to be set explicitly anyway.
            ghostGO.transform.SetParent(null, false);

            GameObject mark = GameObject.Find("LearnerStandingMark");
            Vector3 learnerFeet = mark != null ? mark.transform.position : new Vector3(0f, 0f, -2f);
            if (mark == null)
                Debug.LogWarning("[SignMasterVR] No \"LearnerStandingMark\" in the scene -- falling back to z = -2 for the ghost's placement. Run Step 0 to rebuild the classroom.");

            // 0.45m ahead of the mark, 1.10m up -- roughly where a standing adult's hands sit
            // when signing. The ghost is a TEMPLATE, not a diagram: the learner puts their own
            // hands into it and matches the shape, so it has to be at hand height and within
            // reach, not floating at eye level like a poster.
            //
            // Rotation is identity for the same reason as every other flat surface here: the
            // hand is drawn in this object's local XY plane and reads from its -Z side, and the
            // learner stands at -Z looking towards +Z.
            //
            // The tutor avatar deliberately does NOT sign. There is no per-sign animation on it
            // and no retargeting from the Holistic captures in GestureServer/data/tutor/ --
            // driving a humanoid rig from monocular MediaPipe pose is days of work and usually
            // looks broken. This ghost is the demonstration.
            ghostGO.transform.SetPositionAndRotation(
                learnerFeet + new Vector3(0f, 1.10f, 0.45f), Quaternion.identity);

            var ghostHand = ghostGO.GetComponent<GhostHandPlayer>() ?? ghostGO.AddComponent<GhostHandPlayer>();
            lessonManager.ghostHand = ghostHand;

            EnsureFolder("Assets/StreamingAssets");
            EnsureFolder(GhostHandsFolder);

            EditorUtility.SetDirty(managerGO);
            EditorUtility.SetDirty(ghostGO);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            bool hasClips = Directory.Exists(GhostHandsFolder) &&
                Directory.GetFiles(GhostHandsFolder, "*.json", SearchOption.TopDirectoryOnly).Length > 0;
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log($"[SignMasterVR] GhostHandDemo placed at {ghostGO.transform.position} -- about an arm's length in front of the learner's mark, not six metres away behind the tutor -- and wired to LessonManager.ghostHand. Scene saved. " +
                "If the hand still reads small from the mark, raise \"Scale\" on the GhostHandPlayer component (0.35 by default). " +
                (hasClips
                    ? "Clip files found in Assets/StreamingAssets/GhostHands/ — it should play automatically in Play mode."
                    : "Assets/StreamingAssets/GhostHands/ is empty, so it'll stay hidden for now (that's expected, nothing is broken). " +
                      "In GestureServer/, run: python export_ghost_hand.py --out ../Assets/StreamingAssets/GhostHands " +
                      "-- then let Unity re-import and press Play.") +
                " Save the scene (Ctrl+S).");
        }


        /// <summary>
        /// Step 16 — wire the spoken prompts ("Letter B", "Thank you") to every
        /// GestureData and register them with the AudioManager.
        ///
        /// Clip files are matched to gestureIds by name, using the same
        /// non-alphanumeric-to-underscore rule GhostHandPlayer uses, so
        /// "Can you sign?" -> Can_you_sign.mp3. The match is case-insensitive
        /// because "I Sign" is easy to save as I_sign.mp3.
        ///
        /// Re-runnable: re-recording a clip means dropping the new file in and
        /// running this again.
        /// </summary>
        [MenuItem("Tools/SignMasterVR/16 Assign Prompt Voiceovers")]
        public static void AssignPromptVoiceovers()
        {
            if (!RequireActiveSceneIsNotMainMenu()) return;

            GameObject managerGO = GameObject.Find("LessonSystem");
            if (managerGO == null)
            {
                Debug.LogError("[SignMasterVR] No \"LessonSystem\" GameObject in the active scene. Open ClassRoom.unity and run Step 4 first.");
                return;
            }
            var audio = managerGO.GetComponent<AudioManager>();
            if (audio == null)
            {
                Debug.LogError("[SignMasterVR] \"LessonSystem\" has no AudioManager component. Run Step 4 first.");
                return;
            }

            // Every audio file in the prompts folder, keyed by a normalised name.
            var byName = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { PromptAudioFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                byName[Path.GetFileNameWithoutExtension(path)] = path;
            }
            if (byName.Count == 0)
            {
                Debug.LogError($"[SignMasterVR] No AudioClips found in {PromptAudioFolder}. Drop the recordings in and let Unity import them first.");
                return;
            }

            int assigned = 0;
            var missing = new List<string>();
            var used = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (string guid in AssetDatabase.FindAssets("t:GestureData", new[] { GestureFolder }))
            {
                string gPath = AssetDatabase.GUIDToAssetPath(guid);
                var gesture = AssetDatabase.LoadAssetAtPath<GestureData>(gPath);
                if (gesture == null || string.IsNullOrEmpty(gesture.gestureId)) continue;

                string safe = SafeClipName(gesture.gestureId);
                if (!byName.TryGetValue(safe, out string clipPath))
                {
                    missing.Add($"{gesture.gestureId} (expected {safe}.mp3)");
                    continue;
                }
                used.Add(safe);

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                string key = "Prompt_" + safe;

                gesture.promptAudioKey = key;
                EditorUtility.SetDirty(gesture);

                int at = audio.clips.FindIndex(e => e.key == key);
                var entry = new AudioManager.ClipEntry { key = key, clip = clip };
                if (at >= 0) audio.clips[at] = entry; else audio.clips.Add(entry);
                assigned++;
            }

            var orphans = byName.Keys.Where(n => !used.Contains(n)).OrderBy(n => n).ToList();

            var src = managerGO.GetComponent<AudioSource>();
            if (src == null)
            {
                src = managerGO.AddComponent<AudioSource>();
                src.playOnAwake = false;
                EditorUtility.SetDirty(managerGO);
            }
            audio.source = src;

            EditorUtility.SetDirty(audio);
            AssetDatabase.SaveAssets();

            // Save the scene here rather than asking. The gesture assets save
            // themselves via SaveAssets(), but the clip list lives on
            // LessonSystem in the scene -- so a forgotten Ctrl+S leaves every
            // gesture pointing at a key the AudioManager has never heard of.
            // Half-applied is worse than not applied: it logs 41 misses and
            // plays nothing.
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            string msg = $"[SignMasterVR] Assigned {assigned} spoken prompts and saved {scene.name}.";
            if (missing.Count > 0)
                msg += $"\n  No clip for {missing.Count}: {string.Join(", ", missing)}";
            if (orphans.Count > 0)
                msg += $"\n  {orphans.Count} clip(s) in {PromptAudioFolder} match no gestureId and were ignored: {string.Join(", ", orphans)}";
            Debug.Log(msg);
        }

        /// Same rule as GhostHandPlayer.SafeName, so one naming convention covers
        /// ghost clips and voiceovers alike.
        private static string SafeClipName(string sign)
        {
            var chars = sign.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i])) chars[i] = '_';
            return new string(chars).Trim('_');
        }

        // ------------------------------------------------------------------
        // STEP 17 -- make the shipped scenes survive an actual headset.
        //
        // This exists because of a bug that hid in plain sight: the Main Menu
        // was built as a flat 2D PC menu, and nobody went back for it once
        // the project became a VR build. In the Editor it looked perfect. On
        // a Quest it was BLACK -- a ScreenSpaceOverlay canvas has no surface
        // in the world to draw on, and a bare Camera with no XR Origin above
        // it never receives a head pose. MainMenu is scene 0, so "black on
        // launch" was the whole app.
        //
        // The second half is the exception that filled the device log:
        //   InvalidOperationException: You are trying to read Input using the
        //   UnityEngine.Input class, but you have switched active Input
        //   handling to Input System package in Player Settings.
        // thrown by StandaloneInputModule, once per frame. Both scenes
        // shipped with the legacy module while ProjectSettings has
        // activeInputHandler: 1 (Input System only). Nothing could ever be
        // clicked, and the exception spam alone is a frame-rate disaster.
        //
        // It does NOT touch the XR rig's own canvases (hand menus, tooltips,
        // affordance callouts). An earlier version did, and added raycasters
        // to nine of them; this version removes those again on sight.
        //
        // Idempotent and additive: run it as often as you like. The only
        // thing it ever deletes is the orphaned non-XR camera it replaces and
        // the broken input module it replaces.
        // ------------------------------------------------------------------
        private const string VrRigPrefabPath =
            "Assets/VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Hands Variant.prefab";

        // Step 6 lays every button out against a 1280x720 reference resolution, so giving the
        // converted rect exactly that size preserves the design pixel-for-pixel. 0.001 world
        // units per pixel then makes it a 1.28m x 0.72m panel -- roughly a 58" TV, read
        // comfortably at 1.6m, which is where we place it.
        private static readonly Vector2 MenuCanvasPixelSize = new Vector2(1280f, 720f);
        // 0.00125 m/px puts the menu at 1.60m x 0.90m. It was 0.001 (1.28m x 0.72m), which read
        // as small and far away at 1.6m. Change this one number to resize the whole menu --
        // every child is anchored, so nothing needs re-laying-out.
        private const float MenuCanvasMetresPerPixel = 0.00125f;

        // Only a resting pose so the panel is sensible in the Scene view and in a non-XR Play
        // mode. At runtime WorldPanelPlacer overrides it from the live head pose, because a fixed
        // world position assumes a tracking origin mode and a startup facing direction that we do
        // not control. Rotation is identity: a world-space Canvas is readable from its -Z side,
        // so a panel ahead of a rig at the origin needs no rotation. ClassRoom's LessonCanvas is
        // placed the same way, which is the empirical proof.
        private static readonly Vector3 MenuCanvasFallbackPlacement = new Vector3(0f, 1.35f, 1.6f);

        [MenuItem("Tools/SignMasterVR/17 Make Scenes VR-Ready")]
        public static void MakeScenesVrReady()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[SignMasterVR] Step 17 cancelled -- nothing was changed.");
                return;
            }

            string returnTo = SceneManager.GetActiveScene().path;
            var targets = new[]
            {
                new KeyValuePair<string, bool>("Assets/Scenes/MainMenu.unity", true),
                new KeyValuePair<string, bool>("Assets/Room/Scenes/ClassRoom.unity", false),
            };

            foreach (var target in targets)
            {
                string path = target.Key;
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                {
                    Debug.LogWarning($"[SignMasterVR] {path} not found -- skipped.");
                    continue;
                }

                // Clearing the selection first, for the same reason Step 6 does it: a stale
                // Inspector selection can be redrawn mid-script while a component is briefly
                // absent, and throw a MissingComponentException that looks like our bug.
                Selection.activeObject = null;

                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int changes = UpgradeSceneForVr(scene, target.Value);
                if (changes > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    Debug.Log($"[SignMasterVR] {System.IO.Path.GetFileName(path)}: {changes} change(s) applied and SAVED.");
                }
                else
                {
                    Debug.Log($"[SignMasterVR] {System.IO.Path.GetFileName(path)}: already VR-ready, nothing to change.");
                }
            }

            if (!string.IsNullOrEmpty(returnTo) && AssetDatabase.LoadAssetAtPath<SceneAsset>(returnTo) != null)
                EditorSceneManager.OpenScene(returnTo, OpenSceneMode.Single);

            Debug.Log("[SignMasterVR] Step 17 finished. Build and Run: the menu should now be a panel floating in front of you, and your hands/controllers should get a ray you can point at START LESSON.");
        }

        /// <summary>
        /// Makes one already-open scene work in a headset. Returns the number of changes made so
        /// the caller can skip a pointless re-save.
        ///
        /// Scene-scoped on purpose: it walks scene.GetRootGameObjects() rather than calling
        /// FindObjectsByType, so running it against an additively-loaded scene (which is exactly
        /// how Step 6 builds MainMenu) can never reach into whatever else you had open.
        /// </summary>
        private static int UpgradeSceneForVr(Scene scene, bool isMenu)
        {
            int changes = 0;

            // --- 1. An XR Origin, or the camera never learns where your head is. -------------
            XROrigin origin = FirstInScene<XROrigin>(scene);
            if (origin == null)
            {
                var rigAsset = AssetDatabase.LoadAssetAtPath<GameObject>(VrRigPrefabPath);
                if (rigAsset == null)
                {
                    Debug.LogError($"[SignMasterVR] Cannot find the VR rig prefab at {VrRigPrefabPath}. It ships with the Unity VR template and ClassRoom already uses it, so if it is missing something has deleted it. Skipping \"{scene.name}\" rather than guessing.");
                    return changes;
                }

                var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigAsset);
                if (rig.scene != scene) SceneManager.MoveGameObjectToScene(rig, scene);
                rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                origin = rig.GetComponentInChildren<XROrigin>(true);
                changes++;
                Debug.Log($"[SignMasterVR] {scene.name}: added the XR rig \"{rig.name}\" -- this scene had no XR Origin, so its camera was never head-tracked.");

                // The flat Camera is now a second, untracked camera competing with the rig's.
                // Two enabled cameras at the same depth render in an undefined order, which on
                // device looks like flicker or a frozen view. Only ever removes a root object
                // literally named "Main Camera" that is not part of an XR rig.
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == rig || root.name != "Main Camera") continue;
                    if (root.GetComponentInChildren<XROrigin>(true) != null) continue;
                    Debug.Log($"[SignMasterVR] {scene.name}: removed the old non-XR \"Main Camera\" -- the rig brings its own tracked one.");
                    Object.DestroyImmediate(root);
                    changes++;
                }
            }

            Camera xrCamera = origin != null ? origin.Camera : null;
            if (xrCamera == null && origin != null) xrCamera = origin.GetComponentInChildren<Camera>(true);
            if (xrCamera == null)
                Debug.LogWarning($"[SignMasterVR] {scene.name}: found an XR Origin but no Camera under it. Canvas and audio wiring below will be skipped -- inspect that rig by hand.");

            // --- 2. An AudioListener, or every voiceover plays into the void. ----------------
            if (xrCamera != null && FirstInScene<AudioListener>(scene) == null)
            {
                xrCamera.gameObject.AddComponent<AudioListener>();
                changes++;
                Debug.Log($"[SignMasterVR] {scene.name}: added an AudioListener to \"{xrCamera.name}\". Without one AudioManager plays all 41 prompt clips and nobody hears a thing.");
            }

            // --- 3. The input module. ---------------------------------------------------------
            var eventSystem = FirstInScene<UnityEngine.EventSystems.EventSystem>(scene);
            if (eventSystem == null)
            {
                var esGO = new GameObject("EventSystem");
                SceneManager.MoveGameObjectToScene(esGO, scene);
                eventSystem = esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
                changes++;
                Debug.Log($"[SignMasterVR] {scene.name}: created an EventSystem.");
            }

            var legacyModule = eventSystem.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            if (legacyModule != null)
            {
                Object.DestroyImmediate(legacyModule);
                changes++;
                Debug.Log($"[SignMasterVR] {scene.name}: removed StandaloneInputModule. It reads the legacy UnityEngine.Input API, which this project disables, so it threw InvalidOperationException on every frame.");
            }

            if (eventSystem.GetComponent<XRUIInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<XRUIInputModule>();
                changes++;
                Debug.Log($"[SignMasterVR] {scene.name}: added XRUIInputModule, which is what lets an XR ray drive the UI.");
            }

            // --- 4. Locomotion, and something to stand on. ---------------------------------
            // The rig ships with a CharacterController and a full locomotion stack: Move, Turn,
            // Teleportation, Climb, Grab Move, Jump and Gravity. MainMenu has no colliders at
            // all, so Gravity had the learner falling through an empty world forever -- which
            // from inside the headset looks exactly like the menu flying upwards to infinity.
            //
            // A signing tutor has nowhere to walk to anyway: the learner stands still and signs.
            // So the providers come off in both scenes, and the menu scene gets an invisible
            // floor as a backstop in case one is ever switched back on.
            if (origin != null) changes += DisableLocomotion(origin.transform.root, scene);
            if (isMenu) changes += EnsureMenuFloor(scene);

            // --- 4. Canvases. --------------------------------------------------------------
            // Only OUR canvases. The XR rig prefab carries its own canvases (hand menus,
            // tooltips, affordance callouts) and the first version of this step walked straight
            // into all nine of them, adding raycasters they never asked for. They are the rig
            // vendor's business, not ours.
            Transform rigRoot = origin != null ? origin.transform.root : null;

            foreach (var canvas in AllInScene<Canvas>(scene))
            {
                if (rigRoot != null && canvas.transform.IsChildOf(rigRoot))
                {
                    // Clean up after that earlier mistake. IsAddedComponentOverride is the
                    // discriminator: the rig prefab itself ships none of these, so any one
                    // sitting on a rig child as an instance override was added by us.
                    var strayRaycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
                    if (strayRaycaster != null && PrefabUtility.IsAddedComponentOverride(strayRaycaster))
                    {
                        Debug.Log($"[SignMasterVR] {scene.name}: removed a TrackedDeviceGraphicRaycaster an earlier run of this step wrongly added to the rig's \"{canvas.name}\".");
                        Object.DestroyImmediate(strayRaycaster);
                        changes++;
                    }
                    continue;
                }

                bool isMenuCanvas = isMenu && canvas.name == "MenuCanvas";

                if (canvas.renderMode != RenderMode.WorldSpace)
                {
                    canvas.renderMode = RenderMode.WorldSpace;
                    changes++;
                    Debug.Log($"[SignMasterVR] {scene.name}: \"{canvas.name}\" converted from ScreenSpaceOverlay to World Space. A screen-space canvas has no surface in the world, which is why the headset showed black.");

                    // While the canvas was ScreenSpaceOverlay it DROVE this rect -- Unity owned
                    // its size and position and overwrote them every frame. Changing renderMode
                    // hands control back, but not until the canvas next updates, so writing the
                    // rect in the same breath gets silently clobbered. ForceUpdateCanvases makes
                    // the handover happen now.
                    Canvas.ForceUpdateCanvases();
                }

                if (isMenuCanvas)
                {
                    // Deliberately OUTSIDE the conversion branch above. The first version only
                    // normalised the rect when it converted the canvas, so a canvas converted by
                    // an earlier run kept its stale screen-space values forever -- localPosition
                    // (0, 0, 1.6) sitting on the floor while anchoredPosition still held
                    // (455, 252.5), which is half of a 910x505 Game view. Re-running the step
                    // did nothing because the render mode was already right. Normalise every
                    // time: it is cheap, and it means one more run always fixes it.
                    var rect = canvas.GetComponent<RectTransform>();
                    bool rectWasWrong =
                        rect.sizeDelta != MenuCanvasPixelSize ||
                        rect.anchoredPosition3D != MenuCanvasFallbackPlacement ||
                        rect.localScale != Vector3.one * MenuCanvasMetresPerPixel;

                    rect.anchorMin = new Vector2(0.5f, 0.5f);
                    rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.sizeDelta = MenuCanvasPixelSize;
                    rect.localScale = Vector3.one * MenuCanvasMetresPerPixel;
                    rect.localRotation = Quaternion.identity;

                    // anchoredPosition3D, not position: for a root RectTransform the two mean the
                    // same thing, but writing the RectTransform field keeps the serialized
                    // anchoredPosition and localPosition agreeing with each other. Writing only
                    // position left them contradicting, and the scene reloaded with the panel on
                    // the floor.
                    rect.anchoredPosition3D = MenuCanvasFallbackPlacement;

                    // ScaleWithScreenSize is meaningless in world space -- there is no screen.
                    var scaler = canvas.GetComponent<CanvasScaler>();
                    if (scaler != null) scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

                    if (rectWasWrong)
                    {
                        changes++;
                        Debug.Log($"[SignMasterVR] {scene.name}: \"{canvas.name}\" rect normalised -- {MenuCanvasPixelSize.x}x{MenuCanvasPixelSize.y}px at {MenuCanvasMetresPerPixel}m/px, a {MenuCanvasPixelSize.x * MenuCanvasMetresPerPixel:0.00}m x {MenuCanvasPixelSize.y * MenuCanvasMetresPerPixel:0.00}m panel, resting at {MenuCanvasFallbackPlacement}. WorldPanelPlacer overrides the position at runtime.");
                    }

                    changes += EnsureMenuBackPanel(rect, scene);
                }

                if (xrCamera != null && canvas.worldCamera != xrCamera)
                {
                    canvas.worldCamera = xrCamera;
                    changes++;
                }

                // A fixed world position is a guess about the tracking origin mode AND about
                // which way the learner happens to be facing at startup. Let the MENU panel
                // place itself off the live head pose instead -- see WorldPanelPlacer for why.
                //
                // Only the menu. An earlier version added this to every canvas, which tore
                // LessonCanvas off the classroom's board and left it floating in front of the
                // learner's face. The lesson UI belongs ON the board: it is diegetic, it gives
                // the learner something to turn towards, and it does not follow them around.
                var placer = canvas.GetComponent<WorldPanelPlacer>();
                if (isMenuCanvas && placer == null)
                {
                    canvas.gameObject.AddComponent<WorldPanelPlacer>();
                    changes++;
                    Debug.Log($"[SignMasterVR] {scene.name}: \"{canvas.name}\" got a WorldPanelPlacer, so it appears in front of the learner wherever they are looking when the scene loads.");
                }
                else if (!isMenuCanvas && placer != null)
                {
                    Object.DestroyImmediate(placer);
                    changes++;
                    Debug.Log($"[SignMasterVR] {scene.name}: removed the WorldPanelPlacer an earlier run wrongly put on \"{canvas.name}\" -- that is what made the lesson UI float in front of you instead of staying on the board.");
                }

                if (!isMenu && canvas.name == "LessonCanvas")
                    changes += MountLessonCanvasOnBoard(canvas, scene);

                // GraphicRaycaster only understands a mouse position. TrackedDeviceGraphicRaycaster
                // is the one an XR interactor's ray can hit. Add before removing, so the canvas is
                // never left with no raycaster at all.
                if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                {
                    canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                    changes++;
                    Debug.Log($"[SignMasterVR] {scene.name}: \"{canvas.name}\" got a TrackedDeviceGraphicRaycaster so an XR ray can hit its buttons.");
                }
                var flatRaycaster = canvas.GetComponent<GraphicRaycaster>();
                if (flatRaycaster != null)
                {
                    Object.DestroyImmediate(flatRaycaster);
                    changes++;
                }
            }
            return changes;
        }

        /// <summary>
        /// Sits the lesson UI flat on the classroom's LearningBoard.
        ///
        /// Reads the board's own transform rather than hard-coding coordinates, so moving the
        /// board in the scene moves the UI with it. Deliberately does NOT parent the canvas to
        /// the board: the board is a cube scaled (3.2, 2, 0.05), and a child of a
        /// non-uniformly-scaled transform is sheared. Matching the world transform gets the
        /// same result with none of the distortion.
        ///
        /// Rotation is identity for the same reason as everywhere else in this file: a
        /// world-space Canvas reads from its -Z side, and the learner stands at z = -2 looking
        /// towards +Z.
        /// </summary>
        private static int MountLessonCanvasOnBoard(Canvas canvas, Scene scene)
        {
            GameObject board = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var hit = root.transform.Find("LearningBoard") ?? FindDeep(root.transform, "LearningBoard");
                if (hit != null) { board = hit.gameObject; break; }
            }
            if (board == null)
            {
                Debug.LogWarning($"[SignMasterVR] {scene.name}: no \"LearningBoard\" found, so the lesson UI was left where it is. Run Step 0 to build the classroom, or position LessonCanvas by hand.");
                return 0;
            }

            var rect = canvas.GetComponent<RectTransform>();
            Vector3 boardScale = board.transform.lossyScale;

            // Inset a little so the UI reads as printed on the board rather than overhanging it,
            // and sit just in front of the face so there is no z-fighting with the board itself.
            const float Inset = 0.9f;
            float targetWidth = boardScale.x * Inset;
            float metresPerPixel = targetWidth / Mathf.Max(1f, rect.sizeDelta.x);
            float halfDepth = boardScale.z * 0.5f;

            Vector3 target = board.transform.position - new Vector3(0f, 0f, halfDepth + 0.02f);
            Vector3 targetScale = Vector3.one * metresPerPixel;

            bool changed = (rect.position - target).sqrMagnitude > 1e-6f
                           || (rect.localScale - targetScale).sqrMagnitude > 1e-12f
                           || rect.rotation != Quaternion.identity;
            if (!changed) return 0;

            rect.localScale = targetScale;
            rect.SetPositionAndRotation(target, Quaternion.identity);

            Debug.Log($"[SignMasterVR] {scene.name}: mounted \"{canvas.name}\" on LearningBoard -- {targetWidth:0.00}m wide at {target}, read from the learner's mark at z = -2.");
            return 1;
        }

        /// <summary>Depth-first search for a descendant by name. Transform.Find is one level only.</summary>
        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                var hit = FindDeep(child, name);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>
        /// Switches off every locomotion provider under a rig -- Move, Turn, Teleportation,
        /// Climb, Grab Move, Jump and Gravity, since GravityProvider derives from
        /// LocomotionProvider too. Returns how many it disabled.
        ///
        /// Matches on the component type rather than on GameObject names. The previous
        /// name-matching version looked for "continuousmoveprovider" and friends, which are not
        /// what the objects are called, so it quietly disabled nothing for months while logging
        /// a cheerful success message. A type is checked by the compiler; a guessed name is not.
        ///
        /// Disables the component rather than deactivating its GameObject: those objects also
        /// carry the input action references, and switching the whole object off is a blunter
        /// instrument than this needs to be.
        /// </summary>
        private static int DisableLocomotion(Transform rigRoot, Scene scene)
        {
            if (rigRoot == null) return 0;

            int disabled = 0;
            foreach (var provider in rigRoot.GetComponentsInChildren<LocomotionProvider>(true))
            {
                if (!provider.enabled) continue;
                provider.enabled = false;
                disabled++;
                Debug.Log($"[SignMasterVR] {scene.name}: disabled {provider.GetType().Name} on \"{provider.gameObject.name}\" -- the learner stands still and signs; nothing here should move them.");
            }
            return disabled;
        }

        /// <summary>
        /// Puts an invisible floor under the menu. Belt-and-braces: with the locomotion
        /// providers off nothing should fall, but an empty scene with a CharacterController in
        /// it is one re-enabled component away from an infinite drop, and that failure is
        /// horrible to diagnose from inside a headset -- the world looks fine and the UI simply
        /// recedes upward forever.
        ///
        /// Collider only, no renderer, so it is never seen.
        /// </summary>
        private static int EnsureMenuFloor(Scene scene)
        {
            const string FloorName = "MenuFloor (invisible)";
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == FloorName) return 0;

            var floor = new GameObject(FloorName);
            SceneManager.MoveGameObjectToScene(floor, scene);
            var box = floor.AddComponent<BoxCollider>();
            box.size = new Vector3(40f, 0.2f, 40f);

            // Top face flush with y = 0, so a learner standing on it is at the height the rig
            // expects rather than 10cm above it.
            floor.transform.position = new Vector3(0f, -0.1f, 0f);

            Debug.Log($"[SignMasterVR] {scene.name}: added \"{FloorName}\" -- a 40x40m collider with no renderer, so the rig has something to land on.");
            return 1;
        }

        /// <summary>
        /// Gives the menu an actual background. Floating neon text on nothing is hard to read in
        /// a headset: it sits over whatever happens to be behind it, and with no surface the eye
        /// has nothing to fix convergence on, which is tiring. A dark plate with a thin cyan edge
        /// fixes both and matches the Step 12 neon styling.
        ///
        /// Built as two stretched Images rather than one with an Outline effect, because Outline
        /// offsets the graphic in four directions and goes soft at this scale. Returns the number
        /// of changes so the caller can keep its running total honest.
        /// </summary>
        private static int EnsureMenuBackPanel(RectTransform canvasRect, Scene scene)
        {
            const string BorderName = "MenuBackPanel";
            const string FillName = "MenuBackPanelFill";
            const float BorderThickness = 6f;

            if (canvasRect.Find(BorderName) != null) return 0;

            // Full-bleed to the canvas edge: anchors stretched, zero size delta.
            GameObject border = CreatePanel(canvasRect, BorderName, NeonCyan,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var borderRect = border.GetComponent<RectTransform>();

            GameObject fill = CreatePanel(borderRect, FillName, MenuNearBlack,
                Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-BorderThickness * 2f, -BorderThickness * 2f));

            // Nothing on the back panel should ever swallow a ray meant for a button behind it --
            // and it IS behind them, but raycast-blocking is independent of draw order.
            border.GetComponent<Image>().raycastTarget = false;
            fill.GetComponent<Image>().raycastTarget = false;

            // Behind everything. CreatePanel appends, so without this it would cover the menu.
            border.transform.SetAsFirstSibling();

            Debug.Log($"[SignMasterVR] {scene.name}: added \"{BorderName}\" -- a dark backing plate with a {BorderThickness}px cyan edge, behind all the menu content.");
            return 1;
        }

        /// <summary>First component of type T anywhere in this scene, inactive objects included.</summary>
        private static T FirstInScene<T>(Scene scene) where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var hit = root.GetComponentInChildren<T>(true);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>
        /// Every component of type T in this scene, inactive included. Materialized into a list
        /// before returning because callers add and destroy components while iterating it.
        /// </summary>
        private static List<T> AllInScene<T>(Scene scene) where T : Component
        {
            var found = new List<T>();
            foreach (var root in scene.GetRootGameObjects())
                found.AddRange(root.GetComponentsInChildren<T>(true));
            return found;
        }

        // ------------------------------------------------------------------
        // STEP 18 — Lesson UI for an audience: a Skip button, and the Phase 15
        // debug buttons out of sight.
        //
        // TEST CORRECT / TEST WRONG were the fake-ML harness from before the
        // real recognizer existed. They are wired to FakeGestureRecognizer,
        // which is DISABLED on LessonSystem now -- so on the headset they are
        // two buttons that do nothing at all, sitting in the middle of the
        // lesson UI. Worse than clutter: an examiner will press them.
        //
        // They are deactivated rather than deleted, so re-enabling the fake
        // recognizer for a dry run without a webcam is still one checkbox away.
        //
        // This step also fixes the dead end at the end of a level. CompleteLevel
        // showed a score panel and wrote "Level 2 is now unlocked" to a console
        // that nobody wearing a headset can read -- there was no way to reach
        // Level 2 and no way back to the menu. The panel now carries NEXT LEVEL
        // and MAIN MENU, and the lesson UI carries MAIN MENU throughout.
        // ------------------------------------------------------------------
        [MenuItem("Tools/SignMasterVR/18 Lesson UI: Skip / Menu / Next Level Buttons")]
        public static void BuildLessonUIForDemo()
        {
            if (!RequireActiveSceneIsNotMainMenu()) return;

            GameObject canvasInstance = GameObject.Find("LessonCanvas");
            if (canvasInstance == null)
            {
                Debug.LogError("[SignMasterVR] No \"LessonCanvas\" in the open scene. Open ClassRoom.unity and make sure Step 4 has run.");
                return;
            }
            var canvasRect = canvasInstance.GetComponent<RectTransform>();

            GameObject managerGO = GameObject.Find("LessonSystem");
            var lessonManager = managerGO != null ? managerGO.GetComponent<LessonManager>() : null;
            if (lessonManager == null)
            {
                Debug.LogError("[SignMasterVR] No LessonManager on \"LessonSystem\". Run Step 4 first.");
                return;
            }

            int hidden = 0;
            foreach (var name in new[] { "TestCorrectButton", "TestWrongButton" })
            {
                var t = canvasRect.Find(name);
                if (t == null || !t.gameObject.activeSelf) continue;
                t.gameObject.SetActive(false);
                hidden++;
            }

            // Centred along the bottom, where the two debug buttons used to be. Anchors (0,0)
            // and the default (0.5, 0.5) pivot mean this is the button's CENTRE measured from
            // the canvas's bottom-left, on a 1000x650 canvas -- same convention as Step 3.
            Transform existing = canvasRect.Find("SkipButton");
            bool created = existing == null;
            if (created)
            {
                CreateButton(canvasRect, "SkipButton", "SKIP THIS SIGN",
                    new Vector2(0, 0), new Vector2(0, 0), new Vector2(500, 55), new Vector2(300, 60),
                    MenuPanelDark, SkipAmber);
            }

            // Re-wire every run: the listener is cleared first, so this cannot stack duplicates,
            // and a Skip button whose wiring was lost to a prefab revert silently does nothing.
            WireButton(canvasRect, "SkipButton", lessonManager.SkipCurrentGesture);

            // --- Main Menu, always reachable mid-lesson. ------------------------------------
            // Bottom-left, clear of the Skip button (which spans x = 350..650).
            if (canvasRect.Find("ReturnToMenuButton") == null)
                CreateButton(canvasRect, "ReturnToMenuButton", "MAIN MENU",
                    new Vector2(0, 0), new Vector2(0, 0), new Vector2(160, 55), new Vector2(260, 60),
                    MenuPanelDark, NeonCyan);
            WireButton(canvasRect, "ReturnToMenuButton", lessonManager.GoToMainMenu);

            // --- The level-complete panel was a dead end. -----------------------------------
            // It showed a score and nothing else: no way to Level 2, no way out. Everything
            // below is what turns it into an actual end-of-level screen.
            var completePanel = canvasRect.Find("LevelCompletePanel") as RectTransform;
            if (completePanel == null)
            {
                Debug.LogWarning("[SignMasterVR] No LevelCompletePanel found -- skipping the end-of-level buttons. Re-run Step 3, then Step 4.");
            }
            else
            {
                // The text stretches edge to edge by default and would sit under the buttons.
                // Lift its bottom edge by 120px to make room.
                var completeText = completePanel.Find("LevelCompleteText") as RectTransform;
                if (completeText != null)
                {
                    completeText.anchorMin = Vector2.zero;
                    completeText.anchorMax = Vector2.one;
                    completeText.sizeDelta = new Vector2(0, -120);
                    completeText.anchoredPosition = new Vector2(0, 60);
                }

                // Anchored to the panel's bottom centre, so x is an offset from the middle.
                if (completePanel.Find("NextLevelButton") == null)
                    CreateButton(completePanel, "NextLevelButton", "NEXT LEVEL",
                        new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-170, 55), new Vector2(300, 64),
                        MenuPanelDark, NeonCyan);
                WireButton(completePanel, "NextLevelButton", lessonManager.StartNextLevel);

                if (completePanel.Find("CompleteMenuButton") == null)
                    CreateButton(completePanel, "CompleteMenuButton", "MAIN MENU",
                        new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(170, 55), new Vector2(300, 64),
                        MenuPanelDark, NeonCyan);
                WireButton(completePanel, "CompleteMenuButton", lessonManager.GoToMainMenu);

                // So UIManager can hide NEXT LEVEL on the final level instead of offering a
                // button that quietly does nothing.
                var ui = canvasInstance.GetComponent<UIManager>();
                var nextBtn = completePanel.Find("NextLevelButton");
                if (ui != null && nextBtn != null)
                {
                    ui.nextLevelButton = nextBtn.gameObject;
                    EditorUtility.SetDirty(ui);
                }
            }

            EditorUtility.SetDirty(canvasInstance);
            EditorUtility.SetDirty(lessonManager);
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[SignMasterVR] Lesson UI updated and {scene.name} saved. " +
                      (created ? "Added a \"SKIP THIS SIGN\" button" : "Re-wired the existing Skip button") +
                      $" to LessonManager.SkipCurrentGesture, and hid {hidden} debug button(s). " +
                      "A skip is not scored as correct, so the end-of-level percentage still tells the truth. " +
                      "Also added MAIN MENU to the lesson UI, and NEXT LEVEL + MAIN MENU to the level-complete panel. " +
                      "Check MainMenu and ClassRoom are BOTH in File > Build Profiles > Scenes In Build, or the menu button will do nothing in a build.");
        }

        // ------------------------------------------------------------------
        // STEP 19 — Main menu: a Quit button, and every level actually listed.
        //
        // Two gaps, both only visible once the app was running on a headset.
        // There was no way to leave the application at all -- a sideloaded
        // Quest app with no Quit button leaves the learner pressing the Meta
        // button, which is fine for us and baffling for anyone else.
        //
        // And MainMenuController.levels held only Level 1, because Step 6 was
        // written when Level 1 was all there was. Step 14 registered Level 2
        // with the LevelManager in ClassRoom but nothing taught the MENU about
        // it, so a learner who finished the letters and came back had no way
        // to reach the phrases. This reads whatever LevelData assets exist and
        // lists them all.
        // ------------------------------------------------------------------
        [MenuItem("Tools/SignMasterVR/19 Main Menu: Quit Button + All Levels")]
        public static void MainMenuQuitAndLevels()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            const string scenePath = "Assets/Scenes/MainMenu.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                Debug.LogError($"[SignMasterVR] {scenePath} not found. Run Step 6 first.");
                return;
            }

            string returnTo = SceneManager.GetActiveScene().path;
            Selection.activeObject = null;
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            GameObject canvasGO = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "MenuCanvas") { canvasGO = root; break; }
            if (canvasGO == null)
            {
                Debug.LogError("[SignMasterVR] No \"MenuCanvas\" in MainMenu.unity. Run Step 6 first.");
                return;
            }

            var canvasRect = canvasGO.GetComponent<RectTransform>();
            var menu = canvasGO.GetComponent<MainMenuController>();
            if (menu == null)
            {
                Debug.LogError("[SignMasterVR] MenuCanvas has no MainMenuController. Run Step 6 first.");
                return;
            }

            // --- Quit, top-right corner. ---------------------------------------------------
            // Dark plate with a pink label rather than a solid pink button: Reset Progress is
            // already solid pink, and two identical-looking destructive buttons is a trap.
            if (canvasRect.Find("QuitButton") == null)
                CreateButton(canvasRect, "QuitButton", "QUIT",
                    new Vector2(1, 1), new Vector2(1, 1), new Vector2(-110, -60), new Vector2(170, 56),
                    MenuPanelDark, NeonPink);
            WireButton(canvasRect, "QuitButton", menu.QuitApp);

            // --- Every level the project has, in order. ------------------------------------
            var levelAssets = AssetDatabase.FindAssets("t:LevelData", new[] { "Assets/Data/Levels" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, System.StringComparer.OrdinalIgnoreCase)
                .Select(AssetDatabase.LoadAssetAtPath<LevelData>)
                .Where(l => l != null)
                .ToArray();

            if (levelAssets.Length == 0)
            {
                Debug.LogWarning("[SignMasterVR] No LevelData assets found in Assets/Data/Levels -- the menu's level list was left alone.");
            }
            else
            {
                menu.levels = levelAssets;

                var listPanel = canvasRect.Find("LevelListPanel") as RectTransform;
                var rows = new List<MainMenuController.LevelRow>();
                if (listPanel == null)
                {
                    Debug.LogWarning("[SignMasterVR] No LevelListPanel -- levels are registered but the Levels screen has no buttons for them.");
                }
                else
                {
                    for (int i = 0; i < levelAssets.Length; i++)
                    {
                        var level = levelAssets[i];
                        string btnName = $"Level{i + 1}Button";
                        string statusName = $"Level{i + 1}StatusText";

                        // Rows stack downward from the panel's top edge, 110px apart: button at
                        // -70, its status label 45px under it. Matches Step 6's row 1 exactly,
                        // so an existing Level 1 row is left where it already is.
                        float y = -70f - (110f * i);

                        if (listPanel.Find(btnName) == null)
                            CreateButton(listPanel, btnName, level.levelName.ToUpperInvariant(),
                                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, y), new Vector2(400, 60),
                                MenuPanelDark, NeonCyan);

                        TMP_Text status = null;
                        var existingStatus = listPanel.Find(statusName);
                        if (existingStatus == null)
                            status = CreateText(listPanel, statusName, "", 16, TextAlignmentOptions.Center,
                                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, y - 45f), new Vector2(400, 30), NeonCyan);
                        else
                            status = existingStatus.GetComponent<TMP_Text>();

                        // AddIntPersistentListener, not a closure: a lambda capturing `i` cannot
                        // be serialized into a UnityEvent, so it would survive this Editor
                        // session and be gone from the build.
                        var t = listPanel.Find(btnName);
                        var btn = t != null ? t.GetComponent<Button>() : null;
                        if (btn != null)
                        {
                            for (int k = btn.onClick.GetPersistentEventCount() - 1; k >= 0; k--)
                                UnityEventTools.RemovePersistentListener(btn.onClick, k);
                            UnityEventTools.AddIntPersistentListener(
                                btn.onClick, menu.StartLevelByIndex, i);
                        }

                        rows.Add(new MainMenuController.LevelRow { level = level, statusText = status });
                    }

                    menu.levelRows = rows.ToArray();

                    // Keep BACK below the last row rather than overlapping it.
                    var back = listPanel.Find("BackButton") as RectTransform;
                    float neededHeight = 70f + 110f * levelAssets.Length + 90f;
                    if (listPanel.sizeDelta.y < neededHeight)
                        listPanel.sizeDelta = new Vector2(listPanel.sizeDelta.x, neededHeight);
                    if (back != null)
                    {
                        back.anchorMin = new Vector2(0.5f, 0f);
                        back.anchorMax = new Vector2(0.5f, 0f);
                        back.anchoredPosition = new Vector2(0, 40);
                    }
                }
            }

            EditorUtility.SetDirty(menu);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            if (!string.IsNullOrEmpty(returnTo) && AssetDatabase.LoadAssetAtPath<SceneAsset>(returnTo) != null)
                EditorSceneManager.OpenScene(returnTo, OpenSceneMode.Single);

            Debug.Log($"[SignMasterVR] Main menu updated and saved: QUIT button added, " +
                      $"{levelAssets.Length} level(s) listed ({string.Join(", ", levelAssets.Select(l => l.levelName))}). " +
                      "Quit does nothing in the Editor by design -- it calls EditorApplication.isPlaying = false there so you can still test it.");
        }

        [MenuItem("Tools/SignMasterVR/RUN ALL (0-6)")]
        public static void RunAll()
        {
            BuildClassroomEnvironment();
            GenerateAlphabetLevel();
            BuildTutorPrefab();
            BuildLessonUI();
            WireUpScene();
            ImprovePlayerRig();
            BuildMainMenuScene();
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------
        /// <summary>
        /// Steps 0/4/5 build permanent classroom content (Environment, Tutor, LessonCanvas,
        /// LessonSystem, Player edits) into whatever scene happens to be active — which caused a
        /// real bug: running them while MainMenu.unity was the active scene duplicated the whole
        /// classroom into it. This refuses to run those steps while MainMenu is active instead of
        /// silently building in the wrong place. Always double-check ClassRoom is your active
        /// scene (bold in the Hierarchy) before running Tools > SignMasterVR steps 0/4/5.
        /// </summary>
        private static bool RequireActiveSceneIsNotMainMenu()
        {
            if (SceneManager.GetActiveScene().name == "MainMenu")
            {
                Debug.LogError("[SignMasterVR] Active scene is \"MainMenu\" — this step builds classroom content and must only run while ClassRoom.unity is your active scene. Open/select ClassRoom (it should be bold in the Hierarchy) and run this again.");
                return false;
            }
            return true;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            string folderName = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        private static AnimationClip CreateRotationClip(string name, string childPath, string propertyName, params Keyframe[] keys)
        {
            var clip = new AnimationClip { name = name, legacy = false };
            var curve = new AnimationCurve(keys);
            var binding = EditorCurveBinding.FloatCurve(childPath, typeof(Transform), propertyName);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
            return clip;
        }

        private static void SetLooping(AnimationClip clip, bool loop)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }

        private static void SaveClipAsset(AnimationClip clip, string path)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null)
                AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);
        }

        private static bool ParamExists(AnimatorController controller, string name)
        {
            foreach (var p in controller.parameters)
                if (p.name == name) return true;
            return false;
        }

        private static AnimatorState FindOrAddState(AnimatorStateMachine sm, string name, AnimationClip clip)
        {
            foreach (var s in sm.states)
                if (s.state.name == name) { s.state.motion = clip; return s.state; }
            var state = sm.AddState(name);
            state.motion = clip;
            return state;
        }

        private static void EnsureAnyStateTransition(AnimatorStateMachine sm, AnimatorState target, string triggerParam)
        {
            foreach (var t in sm.anyStateTransitions)
                if (t.destinationState == target) return;
            var transition = sm.AddAnyStateTransition(target);
            transition.hasExitTime = false;
            transition.duration = 0.1f;
            transition.AddCondition(AnimatorConditionMode.If, 0, triggerParam);
        }

        private static void EnsureExitTransition(AnimatorState from, AnimatorState to)
        {
            foreach (var t in from.transitions)
                if (t.destinationState == to) return;
            var transition = from.AddTransition(to);
            transition.hasExitTime = true;
            transition.exitTime = 1f;
            transition.duration = 0.15f;
        }

        private static Transform FindOrCreateChild(Transform parent, string name, PrimitiveType type, Vector3 localPos, Vector3 localScale, Color color)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing;
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            var renderer = go.GetComponent<Renderer>();
            // Build a proper URP-shaded material rather than cloning CreatePrimitive()'s default
            // material — that default can still reference the Built-in pipeline's Standard
            // shader in a URP project, which renders bright pink/magenta (Unity's "incompatible
            // shader" error color) regardless of what color you set on it.
            renderer.sharedMaterial = LoadOrFallback(null, color);
            return go.transform;
        }

        private static TMP_Text CreateText(RectTransform parent, string name, string content, float fontSize,
            TextAlignmentOptions align, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.anchoredPosition = anchoredPos; rt.sizeDelta = sizeDelta;
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = align;
            text.color = color;
            return text;
        }

        private static GameObject CreateImage(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.anchoredPosition = anchoredPos; rt.sizeDelta = sizeDelta;
            var img = go.AddComponent<Image>();
            img.color = Color.white;
            return go;
        }

        private static GameObject CreatePanel(RectTransform parent, string name, Color bg, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            GameObject go = CreateImage(parent, name, anchorMin, anchorMax, anchoredPos, sizeDelta);
            go.GetComponent<Image>().color = bg;
            return go;
        }

        private static Button CreateButton(RectTransform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta, Color bg, Color labelColor)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.anchoredPosition = anchoredPos; rt.sizeDelta = sizeDelta;
            var img = go.AddComponent<Image>();
            img.color = bg;
            var btn = go.AddComponent<Button>();

            GameObject labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(go.transform, false);
            var labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero; labelRT.anchorMax = Vector2.one;
            labelRT.sizeDelta = Vector2.zero; labelRT.anchoredPosition = Vector2.zero;
            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label; tmp.alignment = TextAlignmentOptions.Center; tmp.color = labelColor;
            // Auto-fit instead of a fixed size: wide labels like "TEST WRONG" were overflowing past
            // the button's edges at a flat fontSize 18. This shrinks (never grows) to whatever fits.
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 10;
            tmp.fontSizeMax = 20;

            return btn;
        }

        private static void WireButton(Transform canvasRoot, string buttonName, UnityEngine.Events.UnityAction action)
        {
            Transform t = canvasRoot.Find(buttonName);
            if (t == null) return;
            var btn = t.GetComponent<Button>();
            if (btn == null) return;
            // Avoid stacking duplicate listeners if this step is re-run.
            for (int i = btn.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEventTools.RemovePersistentListener(btn.onClick, i);
            UnityEventTools.AddPersistentListener(btn.onClick, action);
        }

        /// <summary>Loads a material at `path` if it exists; otherwise creates a simple flat-colored one (URP Lit, falling back to Standard) so the environment still builds even if the expected asset is missing.</summary>
        private static Material LoadOrFallback(string path, Color fallbackColor)
        {
            Material mat = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material m = new Material(shader);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", fallbackColor);
            else if (m.HasProperty("_Color")) m.SetColor("_Color", fallbackColor);
            return m;
        }

        /// <summary>Creates (or reuses, by name) a primitive part under `parent`, applying a shared material — used for all the environment's floor/wall/furniture pieces.</summary>
        private static GameObject CreatePart(Transform parent, string name, PrimitiveType type, Vector3 localPos, Vector3 localScale, Material mat, bool removeCollider)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null && mat != null) renderer.sharedMaterial = mat;
            if (removeCollider)
            {
                var col = go.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
            }
            return go;
        }

        /// <summary>True if any Renderer currently in the open scene already uses this exact material asset — used so BuildClassroomEnvironment doesn't lay a second floor/set of walls on top of geometry you already built.</summary>
        private static bool MaterialAlreadyUsedInScene(Material mat)
        {
            if (mat == null) return false;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.sharedMaterial == mat) return true;
            return false;
        }

        /// <summary>Finds a root (or direct child of a root) GameObject by name within a specific loaded scene — used to keep Step 6 idempotent when the MainMenu scene is built additively alongside whatever else is open.</summary>
        private static GameObject FindInScene(Scene scene, string name)
        {
            if (!scene.IsValid()) return null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
                var t = root.transform.Find(name);
                if (t != null) return t.gameObject;
            }
            return null;
        }

        /// <summary>Creates (or reuses, by name) a 3D world-space TextMeshPro label — used for the classroom's branding text. Uses a RectTransform because TextMeshPro (3D) requires one even outside a Canvas.</summary>
        private static void CreateWorldText(Transform parent, string name, string content, Vector3 localPos, float fontSize, Color color)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var rt = go.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = new Vector2(4f, 1f);
            var tmp = go.GetComponent<TextMeshPro>() ?? go.AddComponent<TextMeshPro>();
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color;
        }
    }
}
