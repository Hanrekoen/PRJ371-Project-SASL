using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using SignMasterVR.Lessons;
using SignMasterVR.Tutor;
using SignMasterVR.UI;
using SignMasterVR.Core;
using SignMasterVR.ML;

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
        private const string TutorPrefabPath = PrefabFolder + "/Tutor.prefab";
        private const string CanvasPrefabPath = PrefabFolder + "/LessonCanvas.prefab";
        private const string AnimatorControllerPath = AnimFolder + "/TutorAnimator.controller";
        private const string CharacterTextureFolder = "Assets/Texture_and_materials/Character";
        private const string AlphabetImageFolder = "Assets/Texture_and_materials/Alphabet";
        private const string FeedbackAudioFolder = "Assets/Audio/Feedback";

        // Shared neon UI palette (Lesson canvas + Main Menu cosmetic pass).
        private static readonly Color NeonCyan = new Color(0.05f, 0.95f, 1f);
        private static readonly Color NeonPink = new Color(1f, 0.1f, 0.65f);
        private static readonly Color MenuNearBlack = new Color(0.02f, 0.02f, 0.04f);
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
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
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
        private static readonly string[] LocomotionObjectNames =
        {
            "continuousmoveprovider", "continuousturnprovider", "snapturnprovider",
            "teleportationprovider", "climbprovider", "grabmoveprovider",
            "twohandedgrabmoveprovider", "gravityprovider", "charactercontrollerdriver",
        };

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

            // Belt-and-braces: explicitly disable any locomotion-provider objects by their
            // standard XR Interaction Toolkit names, in case they're still active. This doesn't
            // guess at component types (which vary by package version) — it matches on the
            // well-known default GameObject names, normalized (spaces/hyphens/case ignored), so
            // it's very unlikely to catch anything that isn't actually a locomotion provider.
            int disabledCount = 0;
            foreach (Transform t in player.GetComponentsInChildren<Transform>(true))
            {
                if (!t.gameObject.activeSelf) continue;
                string normalized = Normalize(t.name);
                foreach (var keyword in LocomotionObjectNames)
                {
                    if (normalized == keyword)
                    {
                        t.gameObject.SetActive(false);
                        disabledCount++;
                        Debug.Log($"[SignMasterVR] Disabled locomotion object \"{t.name}\" under Player.");
                        break;
                    }
                }
            }

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
            Debug.Log($"[SignMasterVR] Player moved to the standing mark and rotated to face the tutor. Disabled {disabledCount} locomotion object(s) by name" +
                (cam != null ? $"; Lesson UI now points at \"{cam.name}\"." : ". No camera found under Player — check the rig manually.") +
                " Double-check the Player hierarchy in the Inspector — name-matching is safe but not infallible.");
        }

        private static string Normalize(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in name)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
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
                    es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
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
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; // simple 2D menu — no world-space wiring needed for a PC/Editor demo
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
