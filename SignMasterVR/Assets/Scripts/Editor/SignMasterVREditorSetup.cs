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
    /// ...or just run "RUN ALL (0-4)" once everything below has compiled cleanly.
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

        // ------------------------------------------------------------------
        // STEP 0 — Classroom environment (floor, walls, board, desk, shelves,
        // a standing mark for the learner, and a light if the scene has none)
        // ------------------------------------------------------------------
        [MenuItem("Tools/SignMasterVR/0 Build Classroom Environment")]
        public static void BuildClassroomEnvironment()
        {
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
            canvasRect.sizeDelta = new Vector2(800, 500);
            canvasGO.transform.localScale = Vector3.one * 0.002f; // ~1.6m wide panel in world space

            TMP_Text levelTitle = CreateText(canvasRect, "LevelTitleText", "LEVEL 1\nLETTERS", 28,
                TextAlignmentOptions.TopLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(220, -40), new Vector2(400, 80));
            TMP_Text gestureName = CreateText(canvasRect, "GestureNameText", "A", 64,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(400, 100));
            TMP_Text progressText = CreateText(canvasRect, "ProgressText", "1 / 26", 22,
                TextAlignmentOptions.TopRight, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -40), new Vector2(200, 40));

            GameObject imageGO = CreateImage(canvasRect, "ReferenceImage",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(200, 200));
            Image referenceImage = imageGO.GetComponent<Image>();
            referenceImage.enabled = false;

            GameObject feedbackPanel = CreatePanel(canvasRect, "FeedbackPanel", new Color(0, 0, 0, 0.6f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 90), new Vector2(500, 140));
            TMP_Text feedbackText = CreateText(feedbackPanel.GetComponent<RectTransform>(), "FeedbackText", "", 32,
                TextAlignmentOptions.Center, new Vector2(0, 0.4f), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            TMP_Text feedbackConfidence = CreateText(feedbackPanel.GetComponent<RectTransform>(), "FeedbackConfidenceText", "", 20,
                TextAlignmentOptions.Center, new Vector2(0, 0f), new Vector2(1, 0.4f), Vector2.zero, Vector2.zero);
            feedbackPanel.SetActive(false);

            GameObject levelCompletePanel = CreatePanel(canvasRect, "LevelCompletePanel", new Color(0, 0, 0, 0.85f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 300));
            TMP_Text levelCompleteText = CreateText(levelCompletePanel.GetComponent<RectTransform>(), "LevelCompleteText", "", 30,
                TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            levelCompletePanel.SetActive(false);

            // Debug test buttons (Phase 15 - Fake ML). Wired to FakeGestureRecognizer in Step 4.
            CreateButton(canvasRect, "TestCorrectButton", "TEST CORRECT",
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(110, 40), new Vector2(180, 50), new Color(0.2f, 0.6f, 0.2f));
            CreateButton(canvasRect, "TestWrongButton", "TEST WRONG",
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(310, 40), new Vector2(180, 50), new Color(0.6f, 0.2f, 0.2f));

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
                canvasInstance.transform.position = new Vector3(0, 1.5f, 1.2f);
            }
            Canvas canvas = canvasInstance.GetComponent<Canvas>();
            if (canvas != null && Camera.main != null) canvas.worldCamera = Camera.main;

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

        [MenuItem("Tools/SignMasterVR/RUN ALL (0-4)")]
        public static void RunAll()
        {
            BuildClassroomEnvironment();
            GenerateAlphabetLevel();
            BuildTutorPrefab();
            BuildLessonUI();
            WireUpScene();
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------
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
            var mat = new Material(renderer.sharedMaterial);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            renderer.sharedMaterial = mat;
            return go.transform;
        }

        private static TMP_Text CreateText(RectTransform parent, string name, string content, float fontSize,
            TextAlignmentOptions align, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
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
            text.color = Color.white;
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

        private static Button CreateButton(RectTransform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta, Color bg)
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
            tmp.text = label; tmp.fontSize = 18; tmp.alignment = TextAlignmentOptions.Center; tmp.color = Color.white;

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
