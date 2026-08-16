using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;
using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.Animation;
using UnityEditor;
using UnityEngine;
using Chips = Convai.Editor.UI.ConvaiEditorChips;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Embodiment.Inspectors
{
    /// <summary>
    ///     Convai inspector for <see cref="StandardRigBinding" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Status first.</b> This component is added for the user and, for most characters,
    ///         never edited — the question it is opened to answer is "did Convai recognise this
    ///         character?", not "what shall I type here". So the header reports the answer, the two
    ///         tables show the evidence, and the fields that only a custom rig needs sit below them,
    ///         collapsed. The previous order put eighteen empty object fields between the user and the
    ///         answer.
    ///     </para>
    ///     <para>
    ///         <b>Reads are silent.</b> The tables call the binding's peek methods rather than
    ///         <c>TryGetBone</c> / <c>TryGetBlendshape</c>. The reporting variants write a console
    ///         warning per gap, and a table drawn every repaint would narrate the same gaps to the
    ///         console every time the caches were rebuilt — which this inspector does on every edit.
    ///     </para>
    /// </remarks>
    [CustomEditor(typeof(StandardRigBinding))]
    internal sealed class StandardRigBindingInspector : ConvaiInspectorEditor
    {
        private const string TitleText = "Character Rig";
        private const string SubtitleText = "How Convai finds this character's bones and face shapes";

        private const string SectionTypeId = "CharacterType";
        private const string SectionBonesFoundId = "BonesFound";
        private const string SectionShapesFoundId = "FaceShapesFound";
        private const string SectionCustomSetupId = "CustomRigSetup";
        private const string SectionCalibrationId = "GazeAxisCalibration";

        /// <summary>
        ///     The bones whose absence is worth escalating to the header chip.
        /// </summary>
        /// <remarks>
        ///     Not every semantic bone: <see cref="StandardBone.UpperChest" /> is optional in Unity's
        ///     own humanoid rig and eyes are optional to Gaze, which supports head-only aiming. A chip
        ///     that turned amber for those would be amber on most healthy characters, and a warning
        ///     that is always on is one nobody reads. The tables still report every gap per row.
        /// </remarks>
        private static readonly StandardBone[] CriticalBones =
        {
            StandardBone.Hips, StandardBone.Spine, StandardBone.Head
        };

        private static readonly (StandardBone Semantic, string PropertyName)[] BoneMappings =
        {
            (StandardBone.Hips, "hipsOverride"),
            (StandardBone.Spine, "spineOverride"),
            (StandardBone.Chest, "chestOverride"),
            (StandardBone.UpperChest, "upperChestOverride"),
            (StandardBone.Neck, "neckOverride"),
            (StandardBone.Head, "headOverride"),
            (StandardBone.LeftEye, "leftEyeOverride"),
            (StandardBone.RightEye, "rightEyeOverride"),
            (StandardBone.LeftShoulder, "leftShoulderOverride"),
            (StandardBone.RightShoulder, "rightShoulderOverride"),
            (StandardBone.LeftUpperArm, "leftUpperArmOverride"),
            (StandardBone.RightUpperArm, "rightUpperArmOverride"),
            (StandardBone.LeftUpperLeg, "leftUpperLegOverride"),
            (StandardBone.LeftLowerLeg, "leftLowerLegOverride"),
            (StandardBone.LeftFoot, "leftFootOverride"),
            (StandardBone.RightUpperLeg, "rightUpperLegOverride"),
            (StandardBone.RightLowerLeg, "rightLowerLegOverride"),
            (StandardBone.RightFoot, "rightFootOverride")
        };

        /// <summary>
        ///     The rig types offered in the picker, in the order they are offered.
        /// </summary>
        /// <remarks>
        ///     <see cref="RigConvention.Unknown" /> leads, spelled as what it actually does. Stored as
        ///     the enum's zero it reads as "broken" — the field's first impression on every character
        ///     Convai had recognised perfectly well was the word <c>Unknown</c>, which is the state of
        ///     the <em>override</em>, not of the rig.
        /// </remarks>
        private static readonly RigConvention[] RigTypeValues =
        {
            RigConvention.Unknown,
            RigConvention.ReallusionCC4Extended,
            RigConvention.ReallusionCC3,
            RigConvention.ARKit,
            RigConvention.MetaHuman,
            RigConvention.Custom
        };

        private static readonly GUIContent[] RigTypeOptions =
        {
            new("Detect automatically  (recommended)"),
            new("Reallusion Character Creator 4 (Extended)"),
            new("Reallusion Character Creator 3"),
            new("Apple ARKit"),
            new("Epic MetaHuman"),
            new("Custom — I will map the names myself")
        };

        private static readonly GUIContent TypeSection = new("Character Type");
        private static readonly GUIContent BonesFoundSection = new("Bones Convai Found");
        private static readonly GUIContent ShapesFoundSection = new("Face Shapes Convai Found");
        private static readonly GUIContent CustomSetupSection = new("Custom Rig Setup");
        private static readonly GUIContent CalibrationSection = new("Gaze Axis Calibration");

        private static readonly GUIContent RigTypeLabel = new(
            "Rig Type",
            "Which naming convention this character's face shapes follow. Leave it on Detect " +
            "Automatically unless Convai got it wrong.");
        private static readonly GUIContent FaceMeshesLabel = new(
            "Face Meshes",
            "The skinned meshes Convai drives for expression and lip sync.");
        private static readonly GUIContent CustomMapLabel = new(
            "Custom Convention Map",
            "Maps Convai's semantic face shapes to the names this rig actually uses.");

        private static readonly GUIContent DetectedTypeReading = new("Rig type");
        private static readonly GUIContent MatchReading = new("Match");
        private static readonly GUIContent FaceMeshReading = new("Face meshes");

        private static readonly GUIContent EnableCalibrationLabel = new("Enable Calibration");
        private static readonly GUIContent RootForwardLabel = new("Root Forward (Local)");
        private static readonly GUIContent RootUpLabel = new("Root Up (Local)");
        private static readonly GUIContent LeftEyeForwardLabel = new("Left Eye Forward (Local)");
        private static readonly GUIContent RightEyeForwardLabel = new("Right Eye Forward (Local)");

        private static readonly GUIContent CaptureButton = new(
            "Lock In What Convai Found",
            "Copies the bones listed above into the fields here, so they stay put even if the model " +
            "is re-imported or renamed.");
        private static readonly GUIContent RebuildButton = new(
            "Re-scan This Character",
            "Looks at the character again — use it after swapping a mesh, an outfit or the avatar.");
        private static readonly GUIContent StopPreviewButton = new("Stop Gaze Preview");

        private const string MissingValue = "not found on this rig";

        /// <summary>
        ///     Label-column widths for the two resolution tables, wider than the shared reading
        ///     default.
        /// </summary>
        /// <remarks>
        ///     The default column is sized for short readings ("Match", "Face meshes"). These tables
        ///     are keyed by semantic enum names, and the longest of them — <c>Eye Upper Lid Down
        ///     Right</c> — runs past that column and butts straight up against the value beside it,
        ///     so the two columns read as one run-on string. Overriding per table rather than
        ///     widening the shared token: every other reading in the SDK is short, and moving the
        ///     token would push their values half an inch away from their labels for no reason.
        /// </remarks>
        private const float BoneLabelWidth = 152f;

        /// <inheritdoc cref="BoneLabelWidth" />
        private const float BlendshapeLabelWidth = 188f;

        private static GUIContent[] s_boneLabels;
        private static GUIContent[] s_blendshapeLabels;

        /// <summary>Cached bone labels — the resolved table redraws every repaint.</summary>
        /// <remarks>
        ///     Built on first use rather than in a static field initializer. An
        ///     <see cref="UnityEditor.Editor" /> is a <see cref="ScriptableObject" />, and Unity forbids
        ///     <see cref="ObjectNames.NicifyVariableName" /> during a ScriptableObject's construction —
        ///     so initialising these as static fields made the type initializer throw
        ///     <c>UnityException: FormatVariableName is not allowed to be called from a ScriptableObject
        ///     constructor</c> the first time anything touched this type, taking the whole inspector
        ///     with it. Deferring the call moves it out of that window; the labels are still built once.
        /// </remarks>
        private static GUIContent[] BoneLabels =>
            s_boneLabels ??= BuildEnumLabels<StandardBone>();

        /// <inheritdoc cref="BoneLabels" />
        private static GUIContent[] BlendshapeLabels =>
            s_blendshapeLabels ??= BuildEnumLabels<StandardBlendshape>();

        private readonly Dictionary<Transform, Quaternion> _previewDesiredRotations = new();
        private readonly Dictionary<Transform, Quaternion> _previewRotations = new();

        private SerializedProperty _conventionOverride;
        private SerializedProperty _customConventionMap;
        private SerializedProperty _facialMeshes;
        private SerializedProperty _gazeAxisCalibrationEnabled;
        private SerializedProperty _gazeRootForwardLocal;
        private SerializedProperty _gazeRootUpLocal;
        private SerializedProperty _leftEyeForwardLocal;
        private SerializedProperty _rightEyeForwardLocal;

        private bool _ownsAnimationMode;
        private bool _previewActive;
        private AnimationClip _previewClip;

        private RigSummary _summary;
        private bool _summaryDirty = true;

        protected override string Title => TitleText;
        protected override string Subtitle => SubtitleText;
        protected override string Purpose => _summary.Purpose;

        /// <summary>
        ///     Setup health, in Play mode as well as out of it. The binding is a lookup table, not a
        ///     behaviour — it has no runtime activity, so <c>Live</c> or <c>Idle</c> would report the
        ///     mode the user is already in rather than anything about this character.
        /// </summary>
        protected override GUIContent StatusChip =>
            _summary.Healthy ? Chips.Ready.Content : Chips.NeedsAttention.Content;

        protected override Color StatusChipTint =>
            _summary.Healthy ? Chips.Ready.Tint : Chips.NeedsAttention.Tint;

        protected override void OnEnable()
        {
            base.OnEnable();

            _facialMeshes = serializedObject.FindProperty("facialMeshes");
            _conventionOverride = serializedObject.FindProperty("conventionOverride");
            _customConventionMap = serializedObject.FindProperty("customConventionMap");
            _gazeAxisCalibrationEnabled = serializedObject.FindProperty("gazeAxisCalibrationEnabled");
            _gazeRootForwardLocal = serializedObject.FindProperty("gazeRootForwardLocal");
            _gazeRootUpLocal = serializedObject.FindProperty("gazeRootUpLocal");
            _leftEyeForwardLocal = serializedObject.FindProperty("leftEyeForwardLocal");
            _rightEyeForwardLocal = serializedObject.FindProperty("rightEyeForwardLocal");

            _summaryDirty = true;

            Undo.undoRedoPerformed += HandleUndoRedo;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        protected override void OnDisable()
        {
            RestorePreview();
            Undo.undoRedoPerformed -= HandleUndoRedo;
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;

            base.OnDisable();
        }

        /// <summary>
        ///     Refreshes the cached summary the header reads, and only then. The counts walk both
        ///     semantic enums against the binding's caches; doing that per repaint would be work this
        ///     inspector repeats sixty times a second for an answer that changes on edits alone.
        /// </summary>
        protected override void OnBeforeInspectorGUI()
        {
            if (!_summaryDirty) return;
            _summaryDirty = false;

            var binding = (StandardRigBinding)target;

            // Edit Mode never calls Awake, so without this the first look at a freshly loaded scene
            // reported an unrecognised rig at zero confidence — and every face shape as missing —
            // for a character that was fine.
            binding.EnsureResolutionTables();

            string previousPurpose = _summary.Purpose;
            _summary = RigSummary.Build(binding, (RigConvention)_conventionOverride.intValue);

            // Purpose is cached into the header model rather than read per repaint, so a changed
            // sentence has to be pushed rather than merely returned.
            if (!string.Equals(previousPurpose, _summary.Purpose, StringComparison.Ordinal))
                RebuildInspectorModel();
        }

        protected override void DrawBody()
        {
            var binding = (StandardRigBinding)target;

            EditorGUI.BeginChangeCheck();
            DrawCharacterTypeSection(binding);
            ApplyIfChanged(binding);

            DrawBonesFoundSection(binding);
            DrawFaceShapesFoundSection(binding);

            EditorGUI.BeginChangeCheck();
            DrawCustomRigSetupSection(binding);
            DrawGazeAxisCalibration(binding);
            if (ApplyIfChanged(binding))
                Repaint();
        }

        /// <summary>
        ///     Commits an edit and rebuilds the resolution tables behind it, so the readings drawn
        ///     after this call describe the rig as it is now rather than as it was before the edit.
        /// </summary>
        private bool ApplyIfChanged(StandardRigBinding binding)
        {
            if (!EditorGUI.EndChangeCheck())
                return false;

            serializedObject.ApplyModifiedProperties();
            binding.Rebuild();
            EditorUtility.SetDirty(binding);
            _summaryDirty = true;
            OnBeforeInspectorGUI();
            return true;
        }

        private void DrawCharacterTypeSection(StandardRigBinding binding)
        {
            DrawSection(
                SectionTypeId, TypeSection, Glyphs.Identity,
                () =>
                {
                    Theme.KeyValueRow(
                        DetectedTypeReading,
                        RigConventionDisplay.DisplayName(_summary.Convention),
                        _summary.Convention == RigConvention.Unknown ? Theme.StatusWarn : Theme.StatusReady);

                    Theme.KeyValueRow(
                        MatchReading,
                        _summary.IsManual
                            ? "Set by you"
                            : RigConventionDisplay.MatchStrength(_summary.Convention, _summary.Confidence),
                        _summary.IsManual
                            ? Theme.StatusInfo
                            : RigConventionDisplay.MatchTint(_summary.Convention, _summary.Confidence));

                    Theme.KeyValueRow(FaceMeshReading, _summary.FaceMeshes.ToString());

                    GUILayout.Space(4f);

                    if (_summary.Convention == RigConvention.Unknown)
                    {
                        WarningBox(
                            "Face shapes not recognised",
                            "Convai could not tell which naming this character's face shapes use, so " +
                            "expressions and lip sync may not reach them. Pick the rig type below if " +
                            "you know it, or choose Custom and map the names yourself.");
                    }
                    else if (!_summary.IsManual && _summary.Confidence < RigConventionDisplay.LowConfidence)
                    {
                        WarningBox(
                            "Recognised, but only just",
                            "Only some of this rig type's marker shapes were found. Check that the " +
                            "character's expressions look right; if they do not, set the rig type " +
                            "yourself below.");
                    }

                    DrawRigTypePicker();

                    if (_conventionOverride.intValue == (int)RigConvention.Custom)
                        EditorGUILayout.PropertyField(_customConventionMap, CustomMapLabel);

                    EditorGUILayout.PropertyField(_facialMeshes, FaceMeshesLabel, true);
                    if (_facialMeshes.arraySize == 0)
                    {
                        GUILayout.Label(
                            "Empty — Convai uses every mesh on this character that has face shapes.",
                            Theme.MutedWrapped);
                    }

                    GUILayout.Space(4f);

                    if (GhostButton(RebuildButton))
                    {
                        Undo.RecordObject(binding, "Re-scan Character Rig");
                        binding.Rebuild();
                        EditorUtility.SetDirty(binding);
                        _summaryDirty = true;
                    }
                },
                summary: RigConventionDisplay.ShortName(_summary.Convention));
        }

        /// <summary>
        ///     The rig-type picker, drawn by hand so the stored <see cref="RigConvention.Unknown" />
        ///     can be labelled as the automatic mode it really is. The serialized value is untouched —
        ///     only its wording changes — so existing scenes and prefab overrides are unaffected.
        /// </summary>
        private void DrawRigTypePicker()
        {
            Rect rect = EditorGUILayout.GetControlRect();
            using var scope = new EditorGUI.PropertyScope(rect, RigTypeLabel, _conventionOverride);

            int current = Array.IndexOf(RigTypeValues, (RigConvention)_conventionOverride.intValue);
            if (current < 0) current = 0;

            EditorGUI.BeginChangeCheck();
            int next = EditorGUI.Popup(rect, scope.content, current, RigTypeOptions);
            if (EditorGUI.EndChangeCheck())
                _conventionOverride.intValue = (int)RigTypeValues[next];
        }

        private void DrawBonesFoundSection(StandardRigBinding binding)
        {
            DrawSection(
                SectionBonesFoundId, BonesFoundSection, Glyphs.Animator,
                () =>
                {
                    if (_summary.MissingCriticalBones != null)
                    {
                        WarningBox(
                            "A bone Convai needs is missing",
                            $"This rig has no {_summary.MissingCriticalBones}. Anything that moves it " +
                            "stays inactive. Assign it under Custom Rig Setup below, or use a rig " +
                            "whose humanoid avatar maps it.");
                    }

                    DrawBoneTable(binding);
                },
                summary: $"{_summary.BonesFound} / {_summary.BonesTotal}");
        }

        private void DrawFaceShapesFoundSection(StandardRigBinding binding)
        {
            DrawSection(
                SectionShapesFoundId, ShapesFoundSection, Glyphs.Content,
                () => DrawBlendshapeTable(binding),
                false,
                summary: $"{_summary.ShapesFound} / {_summary.ShapesTotal}");
        }

        private void DrawCustomRigSetupSection(StandardRigBinding binding)
        {
            DrawSection(
                SectionCustomSetupId, CustomSetupSection, Glyphs.Profile,
                () =>
                {
                    InfoBox(
                        "Only needed for custom rigs",
                        "Convai finds these bones on its own for standard characters. Assign one here " +
                        "only to override what it found — it must belong to this character. " +
                        "Resolution order is your assignment, then the humanoid avatar, then a " +
                        "name match.");

                    DrawOverrideFields(binding);

                    if (GhostButton(CaptureButton))
                        CaptureResolvedBones(binding);
                },
                false,
                summary: _summary.ManualBones == 0 ? "None assigned" : $"{_summary.ManualBones} assigned");
        }

        private void DrawGazeAxisCalibration(StandardRigBinding binding)
        {
            DrawSection(
                SectionCalibrationId, CalibrationSection, Glyphs.Blink,
                () =>
                {
                    InfoBox(
                        "Custom rigs only",
                        "Turn this on only for a custom rig whose character or eyes do not face local " +
                        "+Z with +Y up. Left off, Gaze aims against the rig exactly as it does today.");

                    EditorGUILayout.PropertyField(_gazeAxisCalibrationEnabled, EnableCalibrationLabel);
                    if (!_gazeAxisCalibrationEnabled.boolValue) return;

                    EditorGUILayout.PropertyField(_gazeRootForwardLocal, RootForwardLabel);
                    EditorGUILayout.PropertyField(_gazeRootUpLocal, RootUpLabel);
                    EditorGUILayout.PropertyField(_leftEyeForwardLocal, LeftEyeForwardLabel);
                    EditorGUILayout.PropertyField(_rightEyeForwardLocal, RightEyeForwardLabel);

                    bool pair = binding.TryPeekBone(StandardBone.LeftEye, out _) &&
                                binding.TryPeekBone(StandardBone.RightEye, out _);
                    if (!pair)
                    {
                        WarningBox(
                            "Preview needs both eyes",
                            "Axis preview requires a resolved left and right eye bone. Head-only " +
                            "remains supported without preview.");
                        return;
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Center")) PreviewEyeDirection(binding, 0f, 0f);
                        if (GUILayout.Button("Left")) PreviewEyeDirection(binding, -15f, 0f);
                        if (GUILayout.Button("Right")) PreviewEyeDirection(binding, 15f, 0f);
                        if (GUILayout.Button("Up")) PreviewEyeDirection(binding, 0f, 10f);
                        if (GUILayout.Button("Down")) PreviewEyeDirection(binding, 0f, -10f);
                    }

                    if (_previewActive && GhostButton(StopPreviewButton))
                        RestorePreview();
                },
                false,
                summary: _gazeAxisCalibrationEnabled.boolValue ? "On" : "Off");
        }

        private void HandleUndoRedo()
        {
            RestorePreview();
            if (target is not StandardRigBinding binding) return;
            binding.Rebuild();
            _summaryDirty = true;
            Repaint();
        }

        private void HandlePlayModeStateChanged(PlayModeStateChange _)
        {
            RestorePreview();
            _summaryDirty = true;
        }

        private void DrawOverrideFields(StandardRigBinding binding)
        {
            Transform hierarchyRoot = binding.transform;
            for (int i = 0; i < BoneMappings.Length; i++)
            {
                (StandardBone semantic, string propertyName) = BoneMappings[i];
                SerializedProperty property = serializedObject.FindProperty(propertyName);
                EditorGUILayout.PropertyField(property, BoneLabels[(int)semantic]);

                var assigned = property.objectReferenceValue as Transform;
                if (assigned != null && assigned != hierarchyRoot && !assigned.IsChildOf(hierarchyRoot))
                    ErrorBox(
                        "Outside this character",
                        $"{semantic} is outside '{hierarchyRoot.name}' and will be ignored at runtime.");
            }
        }

        private void CaptureResolvedBones(StandardRigBinding binding)
        {
            Undo.RecordObject(binding, "Capture Rig Bone Mappings");
            binding.Rebuild();
            serializedObject.Update();

            for (int i = 0; i < BoneMappings.Length; i++)
            {
                (StandardBone semantic, string propertyName) = BoneMappings[i];
                SerializedProperty property = serializedObject.FindProperty(propertyName);
                if (property.objectReferenceValue == null && binding.TryPeekBone(semantic, out Transform resolved))
                    property.objectReferenceValue = resolved;
            }

            serializedObject.ApplyModifiedProperties();
            binding.Rebuild();
            EditorUtility.SetDirty(binding);
            _summaryDirty = true;
        }

        private static void DrawBoneTable(StandardRigBinding binding)
        {
            foreach (StandardBone bone in Enum.GetValues(typeof(StandardBone)))
            {
                if (!binding.TryPeekBone(bone, out Transform bt))
                {
                    Theme.KeyValueRow(BoneLabels[(int)bone], MissingValue, Theme.StatusWarn, BoneLabelWidth);
                    continue;
                }

                Theme.KeyValueRow(
                    BoneLabels[(int)bone],
                    bt.name + SourceSuffix(binding.GetBoneSource(bone)),
                    null,
                    BoneLabelWidth);
            }
        }

        /// <summary>
        ///     Names the route that supplied a bone, except for the ordinary one.
        /// </summary>
        /// <remarks>
        ///     A humanoid character resolves every bone through its avatar, so tagging that case would
        ///     print the same words eighteen times and say nothing. The two cases worth a word are the
        ///     ones a custom-rig author is here to check: a bone they assigned themselves, and a bone
        ///     matched on its name — a guess that a rename can silently break.
        /// </remarks>
        private static string SourceSuffix(StandardRigBinding.BoneSource source)
        {
            return source switch
            {
                StandardRigBinding.BoneSource.Manual => "     assigned by you",
                StandardRigBinding.BoneSource.NameMatch => "     matched by name",
                _ => string.Empty
            };
        }

        private static void DrawBlendshapeTable(StandardRigBinding binding)
        {
            foreach (StandardBlendshape shape in Enum.GetValues(typeof(StandardBlendshape)))
            {
                bool resolved = binding.TryPeekBlendshape(shape, out SkinnedMeshRenderer mesh, out int idx);
                Theme.KeyValueRow(
                    BlendshapeLabels[(int)shape],
                    resolved ? $"{mesh.name}  [{idx}]" : MissingValue,
                    resolved ? (Color?)null : Theme.StatusWarn,
                    BlendshapeLabelWidth);
            }
        }

        /// <summary>
        ///     Builds one label per enum value, indexed by the value itself. Both semantic enums are
        ///     dense and zero-based, which is what makes the index lookup safe.
        /// </summary>
        private static GUIContent[] BuildEnumLabels<T>() where T : Enum
        {
            Array values = Enum.GetValues(typeof(T));
            var highest = 0;
            foreach (T value in values)
                highest = Mathf.Max(highest, Convert.ToInt32(value));

            var labels = new GUIContent[highest + 1];
            foreach (T value in values)
                labels[Convert.ToInt32(value)] =
                    new GUIContent(ObjectNames.NicifyVariableName(value.ToString()));

            return labels;
        }

        private void PreviewEyeDirection(StandardRigBinding binding, float yaw, float pitch)
        {
            if (!binding.TryPeekBone(StandardBone.LeftEye, out Transform leftEye) ||
                !binding.TryPeekBone(StandardBone.RightEye, out Transform rightEye))
                return;

            if (!_previewActive)
            {
                CapturePreviewRotation(leftEye);
                CapturePreviewRotation(rightEye);
                // Never sample into another tool's AnimationMode session: it owns the
                // preview state and we could not safely restore it.
                if (AnimationMode.InAnimationMode())
                {
                    _previewRotations.Clear();
                    return;
                }

                AnimationMode.StartAnimationMode();
                _ownsAnimationMode = true;
                _previewClip = new AnimationClip { hideFlags = HideFlags.HideAndDontSave };
                _previewActive = true;
            }

            _previewDesiredRotations.Clear();
            Vector3 forward = binding.Root.TransformDirection(_gazeRootForwardLocal.vector3Value);
            Vector3 up = binding.Root.TransformDirection(_gazeRootUpLocal.vector3Value);
            if (forward.sqrMagnitude < 1e-6f || up.sqrMagnitude < 1e-6f) return;

            forward.Normalize();
            up = Vector3.ProjectOnPlane(up, forward);
            if (up.sqrMagnitude < 1e-6f) return;
            up.Normalize();
            Vector3 right = Vector3.Cross(up, forward).normalized;
            Vector3 yawedRight = Quaternion.AngleAxis(yaw, up) * right;
            Quaternion delta = Quaternion.AngleAxis(yaw, up) * Quaternion.AngleAxis(-pitch, yawedRight);

            PreviewEye(leftEye, _leftEyeForwardLocal.vector3Value, delta);
            PreviewEye(rightEye, _rightEyeForwardLocal.vector3Value, delta);
            SamplePreview(binding);
            SceneView.RepaintAll();
        }

        private void PreviewEye(Transform eye, Vector3 localForward, Quaternion delta)
        {
            if (eye == null || localForward.sqrMagnitude < 1e-6f) return;
            Quaternion restLocal = _previewRotations[eye];
            Quaternion parentRotation = eye.parent != null ? eye.parent.rotation : Quaternion.identity;
            Vector3 restForward = parentRotation * restLocal * localForward.normalized;
            Quaternion desiredWorld =
                Quaternion.FromToRotation(restForward, delta * restForward) * (parentRotation * restLocal);
            _previewDesiredRotations[eye] = Quaternion.Inverse(parentRotation) * desiredWorld;
        }

        private void CapturePreviewRotation(Transform transform)
        {
            if (transform != null && !_previewRotations.ContainsKey(transform))
                _previewRotations.Add(transform, transform.localRotation);
        }

        private void SamplePreview(StandardRigBinding binding)
        {
            if (_previewClip == null) return;
            _previewClip.ClearCurves();
            foreach (KeyValuePair<Transform, Quaternion> entry in _previewDesiredRotations)
            {
                if (entry.Key == null) continue;
                string path = AnimationUtility.CalculateTransformPath(entry.Key, binding.transform);
                Quaternion rotation = entry.Value;
                SetPreviewCurve(path, "m_LocalRotation.x", rotation.x);
                SetPreviewCurve(path, "m_LocalRotation.y", rotation.y);
                SetPreviewCurve(path, "m_LocalRotation.z", rotation.z);
                SetPreviewCurve(path, "m_LocalRotation.w", rotation.w);
            }

            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(binding.gameObject, _previewClip, 0f);
            AnimationMode.EndSampling();
        }

        private void SetPreviewCurve(string path, string propertyName, float value)
        {
            _previewClip.SetCurve(path, typeof(Transform), propertyName,
                new AnimationCurve(new Keyframe(0f, value)));
        }

        private void RestorePreview()
        {
            if (!_previewActive) return;
            _previewRotations.Clear();
            _previewDesiredRotations.Clear();
            _previewActive = false;
            if (_ownsAnimationMode && AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            _ownsAnimationMode = false;
            if (_previewClip != null) DestroyImmediate(_previewClip);
            _previewClip = null;
            SceneView.RepaintAll();
        }

        /// <summary>
        ///     Everything the header and the section summaries report, worked out once per edit rather
        ///     than once per repaint.
        /// </summary>
        private readonly struct RigSummary
        {
            private RigSummary(
                RigConvention convention, float confidence, bool isManual, int faceMeshes,
                int bonesFound, int bonesTotal, int shapesFound, int shapesTotal, int manualBones,
                string missingCriticalBones, string purpose)
            {
                Convention = convention;
                Confidence = confidence;
                IsManual = isManual;
                FaceMeshes = faceMeshes;
                BonesFound = bonesFound;
                BonesTotal = bonesTotal;
                ShapesFound = shapesFound;
                ShapesTotal = shapesTotal;
                ManualBones = manualBones;
                MissingCriticalBones = missingCriticalBones;
                Purpose = purpose;
            }

            internal RigConvention Convention { get; }
            internal float Confidence { get; }
            internal bool IsManual { get; }
            internal int FaceMeshes { get; }
            internal int BonesFound { get; }
            internal int BonesTotal { get; }
            internal int ShapesFound { get; }
            internal int ShapesTotal { get; }
            internal int ManualBones { get; }

            /// <summary>Comma-joined critical bones this rig is missing, or null when none are.</summary>
            internal string MissingCriticalBones { get; }

            internal string Purpose { get; }

            /// <summary>Whether the header should read <c>Ready</c> rather than <c>Needs Attention</c>.</summary>
            internal bool Healthy =>
                Convention != RigConvention.Unknown &&
                MissingCriticalBones == null &&
                (IsManual || Confidence >= RigConventionDisplay.LowConfidence);

            internal static RigSummary Build(StandardRigBinding binding, RigConvention overrideValue)
            {
                bool isManual = overrideValue != RigConvention.Unknown;
                RigConvention convention = binding.DetectedConvention;

                int bonesFound = 0, bonesTotal = 0, manualBones = 0;
                List<string> missingCritical = null;
                foreach (StandardBone bone in Enum.GetValues(typeof(StandardBone)))
                {
                    bonesTotal++;
                    if (binding.TryPeekBone(bone, out _))
                    {
                        bonesFound++;
                        if (binding.GetBoneSource(bone) == StandardRigBinding.BoneSource.Manual)
                            manualBones++;
                        continue;
                    }

                    if (Array.IndexOf(CriticalBones, bone) < 0) continue;
                    missingCritical ??= new List<string>();
                    missingCritical.Add(ObjectNames.NicifyVariableName(bone.ToString()));
                }

                int shapesFound = 0, shapesTotal = 0;
                foreach (StandardBlendshape shape in Enum.GetValues(typeof(StandardBlendshape)))
                {
                    shapesTotal++;
                    if (binding.TryPeekBlendshape(shape, out _, out _)) shapesFound++;
                }

                string missing = missingCritical == null
                    ? null
                    : string.Join(", ", missingCritical);

                return new RigSummary(
                    convention,
                    binding.DetectionConfidence,
                    isManual,
                    binding.FacialMeshes?.Count ?? 0,
                    bonesFound,
                    bonesTotal,
                    shapesFound,
                    shapesTotal,
                    manualBones,
                    missing,
                    BuildPurpose(convention, isManual, missing));
            }

            /// <summary>
            ///     The one sentence a beginner has to read. It states what happened, not what the
            ///     component is: someone meeting this inspector wants to know whether their character
            ///     needs anything from them.
            /// </summary>
            private static string BuildPurpose(RigConvention convention, bool isManual, string missing)
            {
                if (convention == RigConvention.Unknown)
                {
                    return "Convai could not tell which naming this character's face shapes use. " +
                           "Expressions and lip sync may not reach them until you pick the rig type below.";
                }

                if (missing != null)
                {
                    return $"Convai recognised this character, but could not find its {missing}. " +
                           "Assign it under Custom Rig Setup below.";
                }

                string name = RigConventionDisplay.DisplayName(convention);
                return isManual
                    ? $"You set this character's rig type to {name}. Convai maps its bones and face " +
                      "shapes to match — nothing else to set up."
                    : $"Convai recognised this character as {name} and mapped its bones and face " +
                      "shapes on its own. There is nothing to set up.";
            }
        }
    }
}
