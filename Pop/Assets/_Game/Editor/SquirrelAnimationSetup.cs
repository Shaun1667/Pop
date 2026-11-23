using System.Linq;
using System.Text;
using Pop.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Pop.EditorTools
{
    /// <summary>
    /// Mixamo로 리깅한 다람쥐와 애니메이션을 준비한다.
    /// - Pop/Dev/Squirrel/1 Setup Mixamo Assets: FBX 임포트 설정(Humanoid, 클립 범위 · 루프) + Animator Controller를 만든다.
    /// - Pop/Dev/Squirrel/2 Replace Player Visual (open scene): 열린 씬의 Player/Visual 아래 다람쥐만 새 모델로 바꾼다.
    ///   Player 계층 밖의 오브젝트는 건드리지 않는다.
    /// </summary>
    public static class SquirrelAnimationSetup
    {
        public const string Dir = "Assets/_Game/Art/Characters/Squirrel/Mixamo";
        public const string ModelPath = Dir + "/Squirrel_Explorer_Mixamo.fbx";
        public const string OldModelPath = "Assets/_Game/Art/Characters/Squirrel/Squirrel_Explorer_Rig.fbx";
        public const string MaterialPath = "Assets/_Game/Art/Characters/Squirrel/Materials/M_Squirrel_Explorer_Rig.mat";
        public const string ControllerPath = Dir + "/Squirrel.controller";

        public const float PlayerHeight = 1.2f;       // 캡슐 키 (m)
        public const float WalkSpeed = 0.9f;          // Happy Walk가 1배속으로 덮는 속도 (m/s, 키 1.2 m 기준)
        public const float RunSpeed = 6f;             // PlayerTuning.moveSpeed
        public const float RunTimeScale = 3.0f;       // Run Forward 재생 배속 (1배속 = 약 1.6 m/s)
        public const float FallVSpeed = -12f;         // 이보다 빨리 떨어지면 Fall 자세

        struct ClipDef
        {
            public string file, clip; public bool loop, bakeY; public int first, last;
            public ClipDef(string f, string c, bool l, bool by, int a = -1, int b = -1) { file = f; clip = c; loop = l; bakeY = by; first = a; last = b; }
        }

        // Jump = Mixamo "Jump"(Jump In Place, 79프레임). 웅크리는 준비(0~23)와 착지 뒤(43~)는 버리고 공중 구간(도약 24 ~ 착지 42)만 쓴다.
        static readonly ClipDef[] Clips =
        {
            new ClipDef("Squirrel@HappyIdle.fbx", "Squirrel_Idle", true, true),
            new ClipDef("Squirrel@HappyWalk.fbx", "Squirrel_Walk", true, true),
            new ClipDef("Squirrel@RunForward.fbx", "Squirrel_Run", true, true),
            new ClipDef("Squirrel@Jump.fbx", "Squirrel_JumpAir", false, false, 24, 42),
            new ClipDef("Squirrel@FallingIdle.fbx", "Squirrel_Fall", true, true),
        };

        [MenuItem("Pop/Dev/Squirrel/1 Setup Mixamo Assets")]
        public static void SetupAssetsMenu() => Debug.Log(SetupAssets());

        [MenuItem("Pop/Dev/Squirrel/2 Replace Player Visual (open scene)")]
        public static void ReplaceMenu() => Debug.Log(ReplaceInActiveScene());

        public static string SetupAssets()
        {
            var log = new StringBuilder();
            AssetDatabase.Refresh();

            // 1) 스킨 모델
            var mi = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (mi == null) return "모델이 없다: " + ModelPath;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            if (mat != null)
                foreach (var kv in mi.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).ToList())
                    mi.RemoveRemap(kv);
            if (mat != null) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Squirrel"), mat);
            mi.SaveAndReimport();
            log.AppendLine(DescribeAvatar(ModelPath));

            // 2) 애니메이션 FBX (스킨 없음). 뼈대만 있어도 Humanoid 아바타를 만들 수 있다.
            foreach (var c in Clips)
            {
                string path = Dir + "/" + c.file;
                var ai = AssetImporter.GetAtPath(path) as ModelImporter;
                if (ai == null) { log.AppendLine("없음: " + path); continue; }
                ai.animationType = ModelImporterAnimationType.Human;
                ai.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                ai.importAnimation = true;
                ai.materialImportMode = ModelImporterMaterialImportMode.None;
                ai.importCameras = false;
                ai.importLights = false;
                ai.SaveAndReimport();

                ai = (ModelImporter)AssetImporter.GetAtPath(path);
                var defs = ai.defaultClipAnimations;
                if (defs == null || defs.Length == 0) { log.AppendLine("클립 없음: " + path); continue; }
                var d = defs[0];
                d.name = c.clip;
                if (c.first >= 0) { d.firstFrame = c.first; d.lastFrame = c.last; }
                d.loopTime = c.loop;
                d.loopPose = c.loop;
                d.lockRootRotation = true;        // 회전은 자세에 굽는다 (Root Motion 안 씀)
                d.keepOriginalOrientation = true;
                d.lockRootPositionXZ = true;
                d.keepOriginalPositionXZ = true;
                d.lockRootHeightY = c.bakeY;      // 점프만 높이를 굽지 않는다: 높이는 물리(캡슐)가 정하고 발이 캡슐 바닥에 맞는다
                d.keepOriginalPositionY = c.bakeY;
                d.heightFromFeet = !c.bakeY;
                ai.clipAnimations = new[] { d };
                ai.SaveAndReimport();
                var clip = LoadClip(path);
                log.AppendLine(DescribeAvatar(path) + (clip != null
                    ? $" clip={clip.name} len={clip.length:F3}s fps={clip.frameRate} loop={clip.isLooping} humanMotion={clip.humanMotion}"
                    : " clip=NULL"));
            }

            log.AppendLine(BuildController());
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        static string DescribeAvatar(string path)
        {
            var av = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            return $"{System.IO.Path.GetFileName(path)}: avatar={(av == null ? "none" : (av.isValid ? "valid" : "INVALID") + (av.isHuman ? "/human" : "/NOT human"))}";
        }

        static AnimationClip LoadClip(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        public static string BuildController()
        {
            var idle = LoadClip(Dir + "/" + Clips[0].file);
            var walk = LoadClip(Dir + "/" + Clips[1].file);
            var run = LoadClip(Dir + "/" + Clips[2].file);
            var jump = LoadClip(Dir + "/" + Clips[3].file);
            var fall = LoadClip(Dir + "/" + Clips[4].file);
            if (idle == null || walk == null || run == null || jump == null || fall == null)
                return "컨트롤러를 못 만들었다: 클립이 빠졌다";

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
                AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter(new AnimatorControllerParameter { name = "Grounded", type = AnimatorControllerParameterType.Bool, defaultBool = true });
            ctrl.AddParameter(new AnimatorControllerParameter { name = "JumpT", type = AnimatorControllerParameterType.Float, defaultFloat = 0.5f });
            ctrl.AddParameter("VSpeed", AnimatorControllerParameterType.Float);

            var sm = ctrl.layers[0].stateMachine;

            // 대기 · 걷기 · 달리기: 바닥 속도로 섞는다
            var loco = ctrl.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, WalkSpeed);
            tree.AddChild(run, RunSpeed);
            var ch = tree.children;
            ch[2].timeScale = RunTimeScale;
            tree.children = ch;
            EditorUtility.SetDirty(tree);

            // 공중: 점프 공중 구간을 JumpT(위쪽 속도)로 직접 넘긴다 → 체공 시간에 자동으로 맞는다
            var air = sm.AddState("Air");
            air.motion = jump;
            air.timeParameterActive = true;
            air.timeParameter = "JumpT";

            var fallState = sm.AddState("Fall");
            fallState.motion = fall;

            sm.defaultState = loco;

            Link(loco, air, 0.06f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            var land = Link(air, loco, 0.10f);
            land.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            land.interruptionSource = TransitionInterruptionSource.Destination; // 착지 직후 다시 점프하면 바로 공중으로
            Link(air, fallState, 0.25f).AddCondition(AnimatorConditionMode.Less, FallVSpeed, "VSpeed");
            var land2 = Link(fallState, loco, 0.12f);
            land2.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            land2.interruptionSource = TransitionInterruptionSource.Destination;
            Link(fallState, air, 0.2f).AddCondition(AnimatorConditionMode.Greater, FallVSpeed * 0.5f, "VSpeed");

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            return $"controller: {ControllerPath} (Locomotion[Idle 0 / Walk {WalkSpeed} / Run {RunSpeed} x{RunTimeScale}], Air[JumpT], Fall)";
        }

        static AnimatorStateTransition Link(AnimatorState from, AnimatorState to, float duration)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = duration;
            t.canTransitionToSelf = false;
            return t;
        }

        static Bounds MeshWorldBounds(SkinnedMeshRenderer smr)
        {
            Bounds mb = smr.sharedMesh.bounds;
            Matrix4x4 m = smr.transform.localToWorldMatrix;
            var b = new Bounds(m.MultiplyPoint3x4(mb.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                b.Encapsulate(m.MultiplyPoint3x4(c));
            }
            return b;
        }

        /// <summary>열린 씬의 Player/Visual 아래 다람쥐를 Mixamo 모델로 바꾸고 저장한다. Player 계층만 건드린다.</summary>
        public static string ReplaceInActiveScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Play 중에는 하지 않는다";
            var scene = EditorSceneManager.GetActiveScene();
            var player = Object.FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
            if (player == null) return scene.name + ": PlayerController가 없다";
            var visual = player.transform.Find("Visual");
            if (visual == null) return scene.name + ": Player/Visual이 없다";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (model == null || ctrl == null) return "모델이나 컨트롤러가 없다 (먼저 1 Setup Mixamo Assets)";

            var log = new StringBuilder(scene.name + ": ");

            // 예전 다람쥐(와 다시 실행했을 때의 새 다람쥐)만 지운다
            for (int i = visual.childCount - 1; i >= 0; i--)
            {
                var child = visual.GetChild(i).gameObject;
                string src = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child);
                if (src == OldModelPath || src == ModelPath)
                {
                    log.Append($"removed '{child.name}' ({System.IO.Path.GetFileName(src)}); ");
                    Undo.DestroyObjectImmediate(child);
                }
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, visual);
            Undo.RegisterCreatedObjectUndo(inst, "Replace squirrel");
            inst.name = "Squirrel";
            var tr = inst.transform;
            tr.localPosition = Vector3.zero;
            tr.localRotation = Quaternion.identity;
            tr.localScale = Vector3.one;

            var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null) return log + "SkinnedMeshRenderer가 없다";
            if (mat != null && smr.sharedMaterial != mat) { smr.sharedMaterial = mat; log.Append("material override; "); }
            smr.updateWhenOffscreen = true;

            // 키 1.2 m, 발이 캡슐 바닥(Visual 원점), 엉덩이가 캡슐 축 위
            // 렌더러 bounds는 여유가 붙어 있어서, 메시 자체(바인드 자세)의 범위로 잰다
            Bounds b = MeshWorldBounds(smr);
            float k = PlayerHeight / Mathf.Max(0.0001f, b.size.y);
            tr.localScale = Vector3.one * k;
            b = MeshWorldBounds(smr);
            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();
            Transform hips = anim.avatar != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (hips == null) hips = inst.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.EndsWith("Hips"));
            Vector3 shift = Vector3.zero;
            shift.y = visual.position.y - b.min.y;
            if (hips != null)
            {
                shift.x = visual.position.x - hips.position.x;
                shift.z = visual.position.z - hips.position.z;
            }
            tr.position += shift;

            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.updateMode = AnimatorUpdateMode.Normal;

            var pav = player.GetComponent<PlayerAnimationVisual>();
            if (pav == null) pav = Undo.AddComponent<PlayerAnimationVisual>(player.gameObject);
            pav.animator = anim;
            EditorUtility.SetDirty(pav);

            var facing = player.GetComponent<PlayerFacingVisual>();
            b = MeshWorldBounds(smr);
            log.Append($"scale local {tr.localScale.x:F4} (mesh world height {b.size.y:F3} m), feet y {b.min.y:F3} vs visual y {visual.position.y:F3}, " +
                       $"local pos {tr.localPosition}, avatar {(anim.avatar != null && anim.avatar.isValid && anim.isHuman ? "humanoid ok" : "PROBLEM")}, " +
                       $"facing.visual {(facing != null && facing.visual == visual ? "ok" : "CHECK")}, stunVisual {(player.GetComponent<PlayerStunVisual>() != null ? "ok" : "none")}, " +
                       $"colliders on player {player.GetComponents<Collider>().Length}");

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            log.Append(saved ? "; saved" : "; SAVE FAILED");
            return log.ToString();
        }
    }
}
